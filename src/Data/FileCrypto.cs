using System;
using System.IO;
using System.Security.Cryptography;
using System.Text;

// 词条文件的密码加密（可选，默认关闭）。不绑定 Windows 账户：密码对，换电脑也能打开；密码丢了无法找回。
// 密钥：PBKDF2-HMAC-SHA256（密码、16 字节随机盐、迭代次数）派生 64 字节，前 32 字节加密、后 32 字节校验
// 加密：AES-256-CBC；校验：HMAC-SHA256，覆盖盐、迭代次数、IV 和密文（先加密后校验）。
// 密码不对、文件被截断或改动，都校验不过。
// 加密后的 phrases.txt 仍是文本，每行都是设置行的样子，旧版本打开时不会把它当成词条显示：
//   #encrypted=1
//   #kdf=pbkdf2-sha256,迭代次数,盐（base64）
//   #data=IV + 密文 + 校验码（base64）
class FileCrypto
{
    public const int DefaultIterations = 200000;
    internal static int NewIterations = DefaultIterations;  // 新设的密码用多少次迭代（自检时调小以加快速度）

    const string Magic = "#encrypted=1";
    const int SaltSize = 16, BlockSize = 16, MacSize = 32;

    readonly byte[] salt;
    readonly int iterations;
    readonly byte[] encKey = new byte[32], macKey = new byte[32];

    FileCrypto(string password, byte[] salt, int iterations)
    {
        this.salt = salt;
        this.iterations = iterations;
        byte[] dk = Pbkdf2(Encoding.UTF8.GetBytes(password), salt, iterations, 64);
        Array.Copy(dk, 0, encKey, 0, 32);
        Array.Copy(dk, 32, macKey, 0, 32);
        Array.Clear(dk, 0, dk.Length);
    }

    // 新设一个密码（随机盐）
    public static FileCrypto Create(string password)
    {
        byte[] salt = new byte[SaltSize];
        using (RNGCryptoServiceProvider rng = new RNGCryptoServiceProvider()) rng.GetBytes(salt);
        return new FileCrypto(password, salt, NewIterations);
    }

    // 文件开头是否是加密格式（只看第一行，不核对内容）
    public static bool IsEncrypted(byte[] file)
    {
        if (file == null) return false;
        string first = FirstLine(file);
        return first == Magic;
    }

    static string FirstLine(byte[] file)
    {
        int start = file.Length >= 3 && file[0] == 0xEF && file[1] == 0xBB && file[2] == 0xBF ? 3 : 0;
        int end = start;
        while (end < file.Length && file[end] != '\r' && file[end] != '\n') end++;
        return Encoding.ASCII.GetString(file, start, end - start);
    }

    // 拆出盐、迭代次数和数据；格式不对（例如被截断）返回 false
    static bool TryParse(byte[] file, out byte[] salt, out int iterations, out byte[] payload)
    {
        salt = null;
        iterations = 0;
        payload = null;
        if (!IsEncrypted(file)) return false;
        string[] lines = Encoding.ASCII.GetString(file).Split(new char[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries);
        if (lines.Length != 3 || !lines[1].StartsWith("#kdf=pbkdf2-sha256,") || !lines[2].StartsWith("#data=")) return false;
        string[] kdf = lines[1].Substring("#kdf=pbkdf2-sha256,".Length).Split(',');
        try
        {
            if (kdf.Length != 2 || !int.TryParse(kdf[0], out iterations) || iterations < 1) return false;
            salt = Convert.FromBase64String(kdf[1]);
            payload = Convert.FromBase64String(lines[2].Substring("#data=".Length));
        }
        catch (FormatException) { return false; }
        int cipherLen = payload.Length - BlockSize - MacSize;
        return salt.Length == SaltSize && cipherLen >= BlockSize && cipherLen % BlockSize == 0;
    }

    // 格式完整（但不知道密码对不对）
    public static bool IsWellFormed(byte[] file)
    {
        byte[] s, p;
        int n;
        return TryParse(file, out s, out n, out p);
    }

    // 用密码打开加密文件：密码对、文件完好时返回 true 并给出明文，crypto 可以继续用来保存。
    // 派生密钥很慢：crypto 传入时若盐和迭代次数相同就直接沿用（同一密码先试主文件、再试备份时只派生一次）
    public static bool TryOpen(byte[] file, string password, ref FileCrypto crypto, out byte[] plain)
    {
        plain = null;
        byte[] salt, payload;
        int iterations;
        if (!TryParse(file, out salt, out iterations, out payload)) return false;
        if (crypto == null || crypto.iterations != iterations || !SameBytes(crypto.salt, salt))
            crypto = new FileCrypto(password, salt, iterations);
        return crypto.TryDecryptPayload(payload, out plain);
    }

    // 用已有的密钥解开另一份同一密码加密的文件（例如备份）；盐不同、被改动或截断时返回 false
    public bool TryDecrypt(byte[] file, out byte[] plain)
    {
        plain = null;
        byte[] s, payload;
        int n;
        if (!TryParse(file, out s, out n, out payload) || n != iterations || !SameBytes(s, salt)) return false;
        return TryDecryptPayload(payload, out plain);
    }

    // 密码是否就是当前这个
    public bool Matches(string password)
    {
        FileCrypto other = new FileCrypto(password, salt, iterations);
        return SameBytes(other.encKey, encKey) && SameBytes(other.macKey, macKey);
    }

    public byte[] Encrypt(byte[] plain)
    {
        byte[] iv = new byte[BlockSize];
        using (RNGCryptoServiceProvider rng = new RNGCryptoServiceProvider()) rng.GetBytes(iv);
        byte[] cipher;
        using (RijndaelManaged aes = NewAes())
        using (ICryptoTransform t = aes.CreateEncryptor(encKey, iv))
            cipher = t.TransformFinalBlock(plain, 0, plain.Length);

        byte[] payload = new byte[BlockSize + cipher.Length + MacSize];
        Array.Copy(iv, 0, payload, 0, BlockSize);
        Array.Copy(cipher, 0, payload, BlockSize, cipher.Length);
        byte[] mac = Mac(payload, BlockSize + cipher.Length);
        Array.Copy(mac, 0, payload, BlockSize + cipher.Length, MacSize);

        string text = Magic + "\r\n" +
                      "#kdf=pbkdf2-sha256," + iterations.ToString(System.Globalization.CultureInfo.InvariantCulture) + "," + Convert.ToBase64String(salt) + "\r\n" +
                      "#data=" + Convert.ToBase64String(payload) + "\r\n";
        return Encoding.ASCII.GetBytes(text);
    }

    bool TryDecryptPayload(byte[] payload, out byte[] plain)
    {
        plain = null;
        int dataLen = payload.Length - MacSize;
        byte[] expected = Mac(payload, dataLen);
        int diff = 0;  // 逐字节比较完，不提前返回
        for (int i = 0; i < MacSize; i++) diff |= expected[i] ^ payload[dataLen + i];
        if (diff != 0) return false;
        try
        {
            byte[] iv = new byte[BlockSize];
            Array.Copy(payload, 0, iv, 0, BlockSize);
            using (RijndaelManaged aes = NewAes())
            using (ICryptoTransform t = aes.CreateDecryptor(encKey, iv))
                plain = t.TransformFinalBlock(payload, BlockSize, dataLen - BlockSize);
            return true;
        }
        catch (CryptographicException) { return false; }
    }

    // 校验码覆盖：盐、迭代次数、IV + 密文
    byte[] Mac(byte[] payload, int length)
    {
        using (HMACSHA256 h = new HMACSHA256(macKey))
        {
            byte[] head = new byte[SaltSize + 4];
            Array.Copy(salt, head, SaltSize);
            head[SaltSize] = (byte)(iterations >> 24);
            head[SaltSize + 1] = (byte)(iterations >> 16);
            head[SaltSize + 2] = (byte)(iterations >> 8);
            head[SaltSize + 3] = (byte)iterations;
            h.TransformBlock(head, 0, head.Length, null, 0);
            h.TransformFinalBlock(payload, 0, length);
            return h.Hash;
        }
    }

    static RijndaelManaged NewAes()
    {
        RijndaelManaged aes = new RijndaelManaged();
        aes.KeySize = 256;
        aes.BlockSize = 128;  // 128 位分组的 Rijndael 就是 AES
        aes.Mode = CipherMode.CBC;
        aes.Padding = PaddingMode.PKCS7;
        return aes;
    }

    static bool SameBytes(byte[] a, byte[] b)
    {
        if (a.Length != b.Length) return false;
        int diff = 0;
        for (int i = 0; i < a.Length; i++) diff |= a[i] ^ b[i];
        return diff == 0;
    }

    // PBKDF2-HMAC-SHA256（RFC 8018）。.NET 4.0 自带的 Rfc2898DeriveBytes 只支持 SHA-1，这里自己实现；
    // 为了几十万次迭代也够快，HMAC 的内外两层中间状态只算一次，每轮只做两次 SHA-256 分组运算
    internal static byte[] Pbkdf2(byte[] password, byte[] salt, int iterations, int length)
    {
        if (password.Length > 64) using (SHA256Managed sha = new SHA256Managed()) password = sha.ComputeHash(password);
        byte[] ipad = new byte[64], opad = new byte[64];
        for (int i = 0; i < 64; i++)
        {
            byte k = i < password.Length ? password[i] : (byte)0;
            ipad[i] = (byte)(k ^ 0x36);
            opad[i] = (byte)(k ^ 0x5c);
        }
        uint[] inner = Sha256.Init(), outer = Sha256.Init();
        uint[] w = new uint[64];
        Sha256.Block(inner, ipad, 0, w);
        Sha256.Block(outer, opad, 0, w);

        byte[] result = new byte[length];
        byte[] block = new byte[64];  // 32 字节摘要 + 填充，长度固定为 64 + 32 字节
        using (HMACSHA256 first = new HMACSHA256(password))
        {
            byte[] msg = new byte[salt.Length + 4];
            Array.Copy(salt, msg, salt.Length);
            for (int index = 1, offset = 0; offset < length; index++, offset += 32)
            {
                msg[salt.Length] = (byte)(index >> 24);
                msg[salt.Length + 1] = (byte)(index >> 16);
                msg[salt.Length + 2] = (byte)(index >> 8);
                msg[salt.Length + 3] = (byte)index;
                byte[] u = first.ComputeHash(msg);
                byte[] t = (byte[])u.Clone();
                uint[] h = new uint[8];
                for (int i = 1; i < iterations; i++)
                {
                    Sha256.HmacOfDigest(inner, outer, u, block, w, h);
                    for (int j = 0; j < 32; j++) t[j] ^= u[j];
                }
                Array.Copy(t, 0, result, offset, Math.Min(32, length - offset));
            }
        }
        return result;
    }

    // 只够 PBKDF2 用的 SHA-256 分组运算（FIPS 180-4）
    static class Sha256
    {
        static readonly uint[] K =
        {
            0x428a2f98, 0x71374491, 0xb5c0fbcf, 0xe9b5dba5, 0x3956c25b, 0x59f111f1, 0x923f82a4, 0xab1c5ed5,
            0xd807aa98, 0x12835b01, 0x243185be, 0x550c7dc3, 0x72be5d74, 0x80deb1fe, 0x9bdc06a7, 0xc19bf174,
            0xe49b69c1, 0xefbe4786, 0x0fc19dc6, 0x240ca1cc, 0x2de92c6f, 0x4a7484aa, 0x5cb0a9dc, 0x76f988da,
            0x983e5152, 0xa831c66d, 0xb00327c8, 0xbf597fc7, 0xc6e00bf3, 0xd5a79147, 0x06ca6351, 0x14292967,
            0x27b70a85, 0x2e1b2138, 0x4d2c6dfc, 0x53380d13, 0x650a7354, 0x766a0abb, 0x81c2c92e, 0x92722c85,
            0xa2bfe8a1, 0xa81a664b, 0xc24b8b70, 0xc76c51a3, 0xd192e819, 0xd6990624, 0xf40e3585, 0x106aa070,
            0x19a4c116, 0x1e376c08, 0x2748774c, 0x34b0bcb5, 0x391c0cb3, 0x4ed8aa4a, 0x5b9cca4f, 0x682e6ff3,
            0x748f82ee, 0x78a5636f, 0x84c87814, 0x8cc70208, 0x90befffa, 0xa4506ceb, 0xbef9a3f7, 0xc67178f2,
        };

        public static uint[] Init()
        {
            return new uint[] { 0x6a09e667, 0xbb67ae85, 0x3c6ef372, 0xa54ff53a, 0x510e527f, 0x9b05688c, 0x1f83d9ab, 0x5be0cd19 };
        }

        static uint Rotr(uint x, int n) { return (x >> n) | (x << (32 - n)); }

        public static void Block(uint[] h, byte[] data, int offset, uint[] w)
        {
            for (int i = 0; i < 16; i++)
                w[i] = (uint)(data[offset + 4 * i] << 24 | data[offset + 4 * i + 1] << 16 | data[offset + 4 * i + 2] << 8 | data[offset + 4 * i + 3]);
            for (int i = 16; i < 64; i++)
            {
                uint s0 = Rotr(w[i - 15], 7) ^ Rotr(w[i - 15], 18) ^ (w[i - 15] >> 3);
                uint s1 = Rotr(w[i - 2], 17) ^ Rotr(w[i - 2], 19) ^ (w[i - 2] >> 10);
                w[i] = w[i - 16] + s0 + w[i - 7] + s1;
            }
            uint a = h[0], b = h[1], c = h[2], d = h[3], e = h[4], f = h[5], g = h[6], hh = h[7];
            for (int i = 0; i < 64; i++)
            {
                uint t1 = hh + (Rotr(e, 6) ^ Rotr(e, 11) ^ Rotr(e, 25)) + ((e & f) ^ (~e & g)) + K[i] + w[i];
                uint t2 = (Rotr(a, 2) ^ Rotr(a, 13) ^ Rotr(a, 22)) + ((a & b) ^ (a & c) ^ (b & c));
                hh = g; g = f; f = e; e = d + t1; d = c; c = b; b = a; a = t1 + t2;
            }
            h[0] += a; h[1] += b; h[2] += c; h[3] += d; h[4] += e; h[5] += f; h[6] += g; h[7] += hh;
        }

        // msg = HMAC(key, msg)，msg 是 32 字节：内外层都已吸收了 64 字节的 key 分组，各再算一个分组
        public static void HmacOfDigest(uint[] inner, uint[] outer, byte[] msg, byte[] block, uint[] w, uint[] h)
        {
            Finish(inner, msg, block, w, h);
            Finish(outer, msg, block, w, h);
        }

        // 从 state 接着算「32 字节 msg + 填充」这一个分组，结果写回 msg
        static void Finish(uint[] state, byte[] msg, byte[] block, uint[] w, uint[] h)
        {
            Array.Copy(state, h, 8);
            Array.Copy(msg, block, 32);
            block[32] = 0x80;
            for (int i = 33; i < 62; i++) block[i] = 0;
            block[62] = 0x03;  // 总长度 (64 + 32) * 8 = 768 位
            block[63] = 0x00;
            Block(h, block, 0, w);
            for (int i = 0; i < 8; i++)
            {
                msg[4 * i] = (byte)(h[i] >> 24);
                msg[4 * i + 1] = (byte)(h[i] >> 16);
                msg[4 * i + 2] = (byte)(h[i] >> 8);
                msg[4 * i + 3] = (byte)h[i];
            }
        }
    }
}
