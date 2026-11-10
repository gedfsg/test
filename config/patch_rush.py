"""Make the discrete branch (rush) start almost always OFF in an ML-Agents checkpoint.pt (no torch needed).
usage: patch_rush.py IN OUT LOGIT_GAP
weight of action_model._discrete_distribution.branches.0 -> 0, bias -> (+gap/2, -gap/2)
=> P(rush) = 1 / (1 + e^gap) in every state. Only tensor bytes and CRC32 fields change."""
import sys, zipfile, struct, zlib, io, pickle, math

src, dst, gap = sys.argv[1], sys.argv[2], float(sys.argv[3])

z = zipfile.ZipFile(src)
pk = [n for n in z.namelist() if n.endswith("data.pkl")][0]
root = pk[:-len("data.pkl")]
class Unp(pickle.Unpickler):
    def find_class(self, mod, name):
        if name == "_rebuild_tensor_v2":
            return lambda storage, off, size, stride, *r: ("TENSOR", storage, off, size)
        if name == "OrderedDict":
            import collections; return collections.OrderedDict
        if mod.startswith("torch") or mod.startswith("collections"):
            return lambda *a, **k: None
        return super().find_class(mod, name)
    def persistent_load(self, pid):
        return ("STORAGE", pid[2], pid[4])
obj = Unp(io.BytesIO(z.read(pk))).load()

def storage_name(param, expect_numel):
    _, (_, key, numel), off, size = obj["Policy"][param]
    assert off == 0 and numel == expect_numel, (param, off, numel)
    n = f"{root}data/{key}"
    info = z.getinfo(n)
    assert info.compress_type == 0 and info.file_size == 4 * numel
    return n, info

pre = "action_model._discrete_distribution.branches.0."
w_name, w_info = storage_name(pre + "weight", 512)
b_name, b_info = storage_name(pre + "bias", 2)
z.close()

buf = bytearray(open(src, "rb").read())

def patch(name, info, new_bytes):
    crc = zlib.crc32(new_bytes) & 0xFFFFFFFF
    h = info.header_offset
    assert buf[h:h+4] == b"PK\x03\x04"
    flags = struct.unpack_from("<H", buf, h + 6)[0]
    nlen, xlen = struct.unpack_from("<HH", buf, h + 26)
    data = h + 30 + nlen + xlen
    assert len(new_bytes) == info.file_size
    buf[data:data+len(new_bytes)] = new_bytes
    struct.pack_into("<I", buf, h + 14, crc)
    if flags & 0x08:
        p = data + len(new_bytes)
        if buf[p:p+4] == b"PK\x07\x08": p += 4
        struct.pack_into("<I", buf, p, crc)
    enc = name.encode(); i = 0; ok = False
    while True:
        i = buf.find(b"PK\x01\x02", i)
        if i < 0: break
        n2, x2, c2 = struct.unpack_from("<HHH", buf, i + 28)
        if bytes(buf[i+46:i+46+n2]) == enc:
            struct.pack_into("<I", buf, i + 16, crc); ok = True; break
        i += 46 + n2 + x2 + c2
    assert ok, name

patch(w_name, w_info, struct.pack("<512f", *([0.0] * 512)))
patch(b_name, b_info, struct.pack("<2f", gap / 2, -gap / 2))
open(dst, "wb").write(buf)

zz = zipfile.ZipFile(dst); bad = zz.testzip()
print("bias ->", struct.unpack("<2f", zz.read(b_name)),
      "| weight absmax ->", max(abs(x) for x in struct.unpack("<512f", zz.read(w_name))),
      f"| P(rush) = {1/(1+math.exp(gap)):.4f}",
      "| zip CRC check:", "OK" if bad is None else bad)
