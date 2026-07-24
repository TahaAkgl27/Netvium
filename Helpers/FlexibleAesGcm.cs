using System;
using System.Buffers.Binary;
using System.Security.Cryptography;

namespace Netvium.Helpers
{
    /// <summary>
    /// AES-GCM decryption for legacy formats that use non-96-bit nonces.
    /// System.Security.Cryptography.AesGcm only accepts 12-byte nonces on Windows,
    /// while mRemoteNG 1.75/1.76 stores a 16-byte nonce.
    /// </summary>
    internal static class FlexibleAesGcm
    {
        private const int BlockSize = 16;

        public static bool TryDecrypt(
            byte[] key,
            byte[] nonce,
            byte[] cipherText,
            byte[] tag,
            byte[] associatedData,
            out byte[] plaintext)
        {
            plaintext = Array.Empty<byte>();
            if (key.Length is not (16 or 24 or 32) || nonce.Length == 0 || tag.Length != BlockSize)
                return false;

            try
            {
                using var aes = Aes.Create();
                aes.Key = key;
                aes.Mode = CipherMode.ECB;
                aes.Padding = PaddingMode.None;
                using var encryptor = aes.CreateEncryptor();

                var hashSubkey = EncryptBlock(encryptor, new byte[BlockSize]);
                var initialCounter = BuildInitialCounter(hashSubkey, nonce);
                var authenticationHash = GHash(hashSubkey, associatedData, cipherText);
                var encryptedCounter = EncryptBlock(encryptor, initialCounter);
                for (var i = 0; i < BlockSize; i++)
                    encryptedCounter[i] ^= authenticationHash[i];

                if (!CryptographicOperations.FixedTimeEquals(encryptedCounter, tag))
                    return false;

                plaintext = new byte[cipherText.Length];
                var counter = (byte[])initialCounter.Clone();
                for (var offset = 0; offset < cipherText.Length; offset += BlockSize)
                {
                    IncrementCounter32(counter);
                    var keyStream = EncryptBlock(encryptor, counter);
                    var count = Math.Min(BlockSize, cipherText.Length - offset);
                    for (var i = 0; i < count; i++)
                        plaintext[offset + i] = (byte)(cipherText[offset + i] ^ keyStream[i]);
                }

                return true;
            }
            catch (Exception ex) when (ex is CryptographicException or ArgumentException)
            {
                if (plaintext.Length > 0)
                    CryptographicOperations.ZeroMemory(plaintext);
                plaintext = Array.Empty<byte>();
                return false;
            }
        }

        private static byte[] BuildInitialCounter(byte[] hashSubkey, byte[] nonce)
        {
            if (nonce.Length == 12)
            {
                var standardCounter = new byte[BlockSize];
                Buffer.BlockCopy(nonce, 0, standardCounter, 0, nonce.Length);
                standardCounter[15] = 1;
                return standardCounter;
            }

            var result = new byte[BlockSize];
            ProcessBlocks(result, hashSubkey, nonce);

            var lengthBlock = new byte[BlockSize];
            BinaryPrimitives.WriteUInt64BigEndian(lengthBlock.AsSpan(8), checked((ulong)nonce.Length * 8));
            ProcessBlock(result, hashSubkey, lengthBlock);
            return result;
        }

        private static byte[] GHash(byte[] hashSubkey, byte[] associatedData, byte[] cipherText)
        {
            var result = new byte[BlockSize];
            ProcessBlocks(result, hashSubkey, associatedData);
            ProcessBlocks(result, hashSubkey, cipherText);

            var lengthBlock = new byte[BlockSize];
            BinaryPrimitives.WriteUInt64BigEndian(lengthBlock.AsSpan(8), checked((ulong)cipherText.Length * 8));
            BinaryPrimitives.WriteUInt64BigEndian(lengthBlock.AsSpan(0, 8), checked((ulong)associatedData.Length * 8));
            ProcessBlock(result, hashSubkey, lengthBlock);
            return result;
        }

        private static void ProcessBlocks(byte[] hash, byte[] hashSubkey, byte[] data)
        {
            for (var offset = 0; offset < data.Length; offset += BlockSize)
            {
                var block = new byte[BlockSize];
                Buffer.BlockCopy(data, offset, block, 0, Math.Min(BlockSize, data.Length - offset));
                ProcessBlock(hash, hashSubkey, block);
            }
        }

        private static void ProcessBlock(byte[] hash, byte[] hashSubkey, byte[] block)
        {
            var productInput = new byte[BlockSize];
            for (var i = 0; i < BlockSize; i++)
                productInput[i] = (byte)(hash[i] ^ block[i]);
            var product = MultiplyGalois(productInput, hashSubkey);
            Buffer.BlockCopy(product, 0, hash, 0, BlockSize);
        }

        private static byte[] MultiplyGalois(byte[] x, byte[] y)
        {
            var product = new byte[BlockSize];
            var value = (byte[])y.Clone();

            for (var bitIndex = 0; bitIndex < 128; bitIndex++)
            {
                if ((x[bitIndex / 8] & (1 << (7 - (bitIndex % 8)))) != 0)
                {
                    for (var i = 0; i < BlockSize; i++)
                        product[i] ^= value[i];
                }

                var leastSignificantBit = (value[15] & 1) != 0;
                byte carry = 0;
                for (var i = 0; i < BlockSize; i++)
                {
                    var nextCarry = (byte)(value[i] & 1);
                    value[i] = (byte)((value[i] >> 1) | (carry << 7));
                    carry = nextCarry;
                }

                if (leastSignificantBit)
                    value[0] ^= 0xE1;
            }

            return product;
        }

        private static void IncrementCounter32(byte[] counter)
        {
            for (var i = 15; i >= 12; i--)
            {
                counter[i]++;
                if (counter[i] != 0)
                    break;
            }
        }

        private static byte[] EncryptBlock(ICryptoTransform encryptor, byte[] input)
        {
            var output = new byte[BlockSize];
            var transformed = encryptor.TransformBlock(input, 0, BlockSize, output, 0);
            if (transformed != BlockSize)
                throw new CryptographicException("AES block encryption returned an invalid length.");
            return output;
        }
    }
}
