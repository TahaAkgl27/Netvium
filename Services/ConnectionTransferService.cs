using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Xml;
using System.Xml.Linq;
using Netvium.Models;
using Netvium.Rdp;

namespace Netvium.Services
{
    public enum ConnectionTransferFileKind
    {
        Unknown,
        Netvium,
        MRemoteNg
    }

    public sealed class ConnectionTransferImportResult
    {
        public ConnectionTransferFileKind FileKind { get; init; }
        public List<ServerInfo> Servers { get; } = new();
        public List<string> Folders { get; } = new();
        public HashSet<string> FavoriteServerKeys { get; } = new(StringComparer.OrdinalIgnoreCase);
        public bool PasswordRequired { get; init; }
        public int SkippedUnsupportedProtocols { get; set; }
        public int SkippedInvalidEntries { get; set; }
        public int CredentialsNotImported { get; set; }
        public string? ErrorMessage { get; init; }
        public bool Succeeded => !PasswordRequired && string.IsNullOrWhiteSpace(ErrorMessage);
    }

    /// <summary>
    /// Imports/exports portable Netvium connection XML files and imports mRemoteNG 2.6 XML files.
    /// Netvium passwords are protected with an export password using PBKDF2-SHA256 + AES-256-GCM.
    /// </summary>
    public static class ConnectionTransferService
    {
        private const int NetviumKdfIterations = 150_000;
        private const int MaxImportBytes = 25 * 1024 * 1024;
        private const string NetviumVerifierText = "NetviumConnections:v1";
        private const string MRemoteDefaultMasterPassword = "mR3m";

        public static async Task<int> ExportNetviumAsync(
            string path,
            IEnumerable<ServerInfo> servers,
            IEnumerable<string> folders,
            IEnumerable<string> favoriteServerKeys,
            string exportPassword,
            CancellationToken cancellationToken = default)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(path);
            if (string.IsNullOrWhiteSpace(exportPassword))
                throw new ArgumentException("An export password is required.", nameof(exportPassword));

            var serverList = servers.Where(s => !s.IsEphemeral).ToList();
            var favorites = new HashSet<string>(favoriteServerKeys, StringComparer.OrdinalIgnoreCase);
            var salt = RandomNumberGenerator.GetBytes(16);
            var key = Rfc2898DeriveBytes.Pbkdf2(
                exportPassword,
                salt,
                NetviumKdfIterations,
                HashAlgorithmName.SHA256,
                32);

            try
            {
                var verifier = EncryptAesGcm(Encoding.UTF8.GetBytes(NetviumVerifierText), key);
                var root = new XElement("NetviumConnections",
                    new XAttribute("Version", "1"),
                    new XAttribute("ExportedAtUtc", DateTime.UtcNow.ToString("O", CultureInfo.InvariantCulture)),
                    new XAttribute("Encryption", "AES-256-GCM"),
                    new XAttribute("Kdf", "PBKDF2-SHA256"));

                root.Add(new XElement("Security",
                    new XAttribute("Iterations", NetviumKdfIterations),
                    new XAttribute("Salt", Convert.ToBase64String(salt)),
                    new XAttribute("Verifier", Convert.ToBase64String(verifier.CipherText)),
                    new XAttribute("VerifierNonce", Convert.ToBase64String(verifier.Nonce)),
                    new XAttribute("VerifierTag", Convert.ToBase64String(verifier.Tag))));

                var allFolders = folders
                    .Concat(serverList.Select(s => s.Folder))
                    .Where(f => !string.IsNullOrWhiteSpace(f))
                    .Select(f => f.Trim())
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .OrderBy(f => f, StringComparer.CurrentCultureIgnoreCase);
                root.Add(new XElement("Folders", allFolders.Select(f => new XElement("Folder", new XAttribute("Name", f)))));

                var connectionElements = new List<XElement>(serverList.Count);
                foreach (var server in serverList)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    var port = NormalizePort(server.Port);
                    var element = new XElement("Connection",
                        new XAttribute("Name", server.Name ?? string.Empty),
                        new XAttribute("Host", server.Ip ?? string.Empty),
                        new XAttribute("Username", server.Username ?? string.Empty),
                        new XAttribute("Folder", string.IsNullOrWhiteSpace(server.Folder) ? "Sahsi" : server.Folder),
                        new XAttribute("Port", port),
                        new XAttribute("Scale", server.Scale > 0 ? server.Scale : 100),
                        new XAttribute("ReadabilityMode", server.ReadabilityMode),
                        new XAttribute("ResolutionProfile", server.ResolutionProfile),
                        new XAttribute("Favorite", favorites.Contains(GetEndpointKey(server.Ip, port))));

                    if (!string.IsNullOrEmpty(server.Password))
                    {
                        var encrypted = EncryptAesGcm(Encoding.UTF8.GetBytes(server.Password), key);
                        element.Add(new XElement("Password",
                            new XAttribute("Cipher", Convert.ToBase64String(encrypted.CipherText)),
                            new XAttribute("Nonce", Convert.ToBase64String(encrypted.Nonce)),
                            new XAttribute("Tag", Convert.ToBase64String(encrypted.Tag))));
                    }

                    connectionElements.Add(element);
                }

                root.Add(new XElement("Connections", connectionElements));
                var document = new XDocument(new XDeclaration("1.0", "utf-8", null), root);

                var fullPath = Path.GetFullPath(path);
                var directory = Path.GetDirectoryName(fullPath);
                if (!string.IsNullOrWhiteSpace(directory))
                    Directory.CreateDirectory(directory);

                await using var stream = new FileStream(fullPath, FileMode.Create, FileAccess.Write, FileShare.None, 81920, useAsync: true);
                await document.SaveAsync(stream, SaveOptions.None, cancellationToken).ConfigureAwait(false);
                await stream.FlushAsync(cancellationToken).ConfigureAwait(false);
                return serverList.Count;
            }
            finally
            {
                CryptographicOperations.ZeroMemory(key);
            }
        }

        public static async Task<ConnectionTransferImportResult> ImportAsync(
            string path,
            string? transferPassword = null,
            CancellationToken cancellationToken = default)
        {
            try
            {
                var document = await LoadSafeXmlAsync(path, cancellationToken).ConfigureAwait(false);
                var root = document.Root;
                if (root == null)
                    return Failure(ConnectionTransferFileKind.Unknown, "XML file is empty.");

                return root.Name.LocalName switch
                {
                    "NetviumConnections" => ImportNetvium(root, transferPassword),
                    "Connections" => ImportMRemoteNg(root, transferPassword),
                    _ => Failure(ConnectionTransferFileKind.Unknown, "This XML file is not a supported Netvium or mRemoteNG connection file.")
                };
            }
            catch (FileNotFoundException)
            {
                return Failure(ConnectionTransferFileKind.Unknown, "The selected file could not be found.");
            }
            catch (InvalidDataException ex)
            {
                return Failure(ConnectionTransferFileKind.Unknown, ex.Message);
            }
            catch (XmlException ex)
            {
                return Failure(ConnectionTransferFileKind.Unknown, $"The XML file is invalid: {ex.Message}");
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or CryptographicException or FormatException)
            {
                return Failure(ConnectionTransferFileKind.Unknown, $"The connection file could not be read: {ex.Message}");
            }
        }

        private static ConnectionTransferImportResult ImportNetvium(XElement root, string? password)
        {
            if (!string.Equals((string?)root.Attribute("Version"), "1", StringComparison.Ordinal))
                return Failure(ConnectionTransferFileKind.Netvium, "This Netvium connection file version is not supported.");

            var security = root.Element("Security");
            if (security == null)
                return Failure(ConnectionTransferFileKind.Netvium, "The Netvium connection file has no security information.");

            if (string.IsNullOrEmpty(password))
                return PasswordNeeded(ConnectionTransferFileKind.Netvium);

            var iterations = ParseInt((string?)security.Attribute("Iterations"), NetviumKdfIterations);
            if (iterations < 10_000 || iterations > 2_000_000)
                return Failure(ConnectionTransferFileKind.Netvium, "The connection file uses an invalid password derivation setting.");

            var salt = ReadBase64Attribute(security, "Salt");
            var verifierCipher = ReadBase64Attribute(security, "Verifier");
            var verifierNonce = ReadBase64Attribute(security, "VerifierNonce");
            var verifierTag = ReadBase64Attribute(security, "VerifierTag");
            if (salt == null || verifierCipher == null || verifierNonce == null || verifierTag == null)
                return Failure(ConnectionTransferFileKind.Netvium, "The security information in the connection file is incomplete.");

            var key = Rfc2898DeriveBytes.Pbkdf2(password, salt, iterations, HashAlgorithmName.SHA256, 32);
            try
            {
                if (!TryDecryptAesGcm(verifierCipher, verifierNonce, verifierTag, key, out var verifier)
                    || !CryptographicOperations.FixedTimeEquals(verifier, Encoding.UTF8.GetBytes(NetviumVerifierText)))
                {
                    return PasswordNeeded(ConnectionTransferFileKind.Netvium, "The transfer password is incorrect.");
                }

                var result = new ConnectionTransferImportResult { FileKind = ConnectionTransferFileKind.Netvium };
                foreach (var folder in root.Element("Folders")?.Elements("Folder") ?? Enumerable.Empty<XElement>())
                {
                    var name = ((string?)folder.Attribute("Name"))?.Trim();
                    if (!string.IsNullOrWhiteSpace(name) && !result.Folders.Contains(name, StringComparer.OrdinalIgnoreCase))
                        result.Folders.Add(name);
                }

                foreach (var element in root.Element("Connections")?.Elements("Connection") ?? Enumerable.Empty<XElement>())
                {
                    var host = ((string?)element.Attribute("Host"))?.Trim() ?? string.Empty;
                    if (string.IsNullOrWhiteSpace(host))
                    {
                        result.SkippedInvalidEntries++;
                        continue;
                    }

                    var name = ((string?)element.Attribute("Name"))?.Trim();
                    var folder = ((string?)element.Attribute("Folder"))?.Trim();
                    var port = NormalizePort(ParseInt((string?)element.Attribute("Port"), 3389));
                    var server = new ServerInfo(
                        string.IsNullOrWhiteSpace(name) ? host : name,
                        host,
                        ((string?)element.Attribute("Username"))?.Trim() ?? string.Empty,
                        string.Empty,
                        string.IsNullOrWhiteSpace(folder) ? "Sahsi" : folder,
                        port,
                        Math.Max(1, ParseInt((string?)element.Attribute("Scale"), 100)));

                    if (Enum.TryParse((string?)element.Attribute("ReadabilityMode"), true, out RdpReadabilityMode readability))
                        server.ReadabilityMode = readability;
                    if (Enum.TryParse((string?)element.Attribute("ResolutionProfile"), true, out RdpResolutionProfile resolution))
                        server.ResolutionProfile = resolution;

                    var passwordElement = element.Element("Password");
                    if (passwordElement != null)
                    {
                        var cipher = ReadBase64Attribute(passwordElement, "Cipher");
                        var nonce = ReadBase64Attribute(passwordElement, "Nonce");
                        var tag = ReadBase64Attribute(passwordElement, "Tag");
                        if (cipher == null || nonce == null || tag == null
                            || !TryDecryptAesGcm(cipher, nonce, tag, key, out var plainPassword))
                        {
                            server.StoredPasswordUnavailable = true;
                            result.CredentialsNotImported++;
                        }
                        else
                        {
                            server.Password = Encoding.UTF8.GetString(plainPassword);
                            CryptographicOperations.ZeroMemory(plainPassword);
                        }
                    }

                    result.Servers.Add(server);
                    if (ParseBool((string?)element.Attribute("Favorite")))
                        result.FavoriteServerKeys.Add(GetEndpointKey(host, port));
                    if (!result.Folders.Contains(server.Folder, StringComparer.OrdinalIgnoreCase))
                        result.Folders.Add(server.Folder);
                }

                return result;
            }
            finally
            {
                CryptographicOperations.ZeroMemory(key);
            }
        }

        private static ConnectionTransferImportResult ImportMRemoteNg(XElement root, string? suppliedPassword)
        {
            if (ParseBool((string?)root.Attribute("FullFileEncryption")))
                return Failure(ConnectionTransferFileKind.MRemoteNg, "Fully encrypted mRemoteNG files are not supported. Export the connections with Full File Encryption disabled.");

            var engine = (string?)root.Attribute("EncryptionEngine") ?? "AES";
            var mode = (string?)root.Attribute("BlockCipherMode") ?? "GCM";
            if (!engine.Equals("AES", StringComparison.OrdinalIgnoreCase)
                || !mode.Equals("GCM", StringComparison.OrdinalIgnoreCase))
            {
                return Failure(ConnectionTransferFileKind.MRemoteNg, $"mRemoteNG encryption {engine}/{mode} is not supported. Use AES/GCM when exporting.");
            }

            var iterations = ParseInt((string?)root.Attribute("KdfIterations"), 1000);
            if (iterations < 1 || iterations > 2_000_000)
                return Failure(ConnectionTransferFileKind.MRemoteNg, "The mRemoteNG file uses an invalid KDF iteration count.");

            var masterPassword = suppliedPassword ?? MRemoteDefaultMasterPassword;
            var protectedValue = (string?)root.Attribute("Protected");
            if (!string.IsNullOrWhiteSpace(protectedValue)
                && !TryDecryptMRemoteValue(protectedValue, masterPassword, iterations, out _))
            {
                return PasswordNeeded(
                    ConnectionTransferFileKind.MRemoteNg,
                    suppliedPassword == null ? null : "The mRemoteNG master password is incorrect.");
            }

            var result = new ConnectionTransferImportResult { FileKind = ConnectionTransferFileKind.MRemoteNg };
            var initialState = new MRemoteInheritedValues("RDP", "", "", "", 3389);
            foreach (var node in root.Elements().Where(e => e.Name.LocalName == "Node"))
                ReadMRemoteNode(node, new List<string>(), initialState, masterPassword, iterations, result);
            return result;
        }

        private static void ReadMRemoteNode(
            XElement node,
            List<string> parentFolders,
            MRemoteInheritedValues parent,
            string masterPassword,
            int iterations,
            ConnectionTransferImportResult result)
        {
            var state = new MRemoteInheritedValues(
                ResolveMRemoteValue(node, "Protocol", "InheritProtocol", parent.Protocol, "RDP"),
                ResolveMRemoteValue(node, "Username", "InheritUsername", parent.Username, string.Empty),
                ResolveMRemoteValue(node, "Domain", "InheritDomain", parent.Domain, string.Empty),
                ResolveMRemoteValue(node, "Password", "InheritPassword", parent.EncryptedPassword, string.Empty),
                NormalizePort(ParseInt(ResolveMRemoteValue(node, "Port", "InheritPort", parent.Port.ToString(CultureInfo.InvariantCulture), "3389"), 3389)));

            var type = ((string?)node.Attribute("Type"))?.Trim();
            if (string.Equals(type, "Container", StringComparison.OrdinalIgnoreCase))
            {
                var containerName = ((string?)node.Attribute("Name"))?.Trim();
                var childFolders = new List<string>(parentFolders);
                if (!string.IsNullOrWhiteSpace(containerName))
                    childFolders.Add(containerName);
                var folderPath = BuildFolderPath(childFolders);
                if (!string.IsNullOrWhiteSpace(folderPath) && !result.Folders.Contains(folderPath, StringComparer.OrdinalIgnoreCase))
                    result.Folders.Add(folderPath);

                foreach (var child in node.Elements().Where(e => e.Name.LocalName == "Node"))
                    ReadMRemoteNode(child, childFolders, state, masterPassword, iterations, result);
                return;
            }

            if (!string.Equals(type, "Connection", StringComparison.OrdinalIgnoreCase))
            {
                result.SkippedInvalidEntries++;
                return;
            }

            if (!state.Protocol.Equals("RDP", StringComparison.OrdinalIgnoreCase))
            {
                result.SkippedUnsupportedProtocols++;
                return;
            }

            var host = ((string?)node.Attribute("Hostname"))?.Trim() ?? string.Empty;
            if (string.IsNullOrWhiteSpace(host))
            {
                result.SkippedInvalidEntries++;
                return;
            }

            var name = ((string?)node.Attribute("Name"))?.Trim();
            var username = state.Username.Trim();
            if (!string.IsNullOrWhiteSpace(state.Domain)
                && !username.Contains('\\')
                && !username.Contains('@'))
            {
                username = $"{state.Domain.Trim()}\\{username}";
            }

            var password = string.Empty;
            var passwordUnavailable = false;
            if (!string.IsNullOrWhiteSpace(state.EncryptedPassword)
                && !TryDecryptMRemoteValue(state.EncryptedPassword, masterPassword, iterations, out password))
            {
                passwordUnavailable = true;
                result.CredentialsNotImported++;
            }

            var folder = BuildFolderPath(parentFolders);
            var server = new ServerInfo(
                string.IsNullOrWhiteSpace(name) ? host : name,
                host,
                username,
                password,
                string.IsNullOrWhiteSpace(folder) ? "Sahsi" : folder,
                state.Port)
            {
                StoredPasswordUnavailable = passwordUnavailable
            };
            result.Servers.Add(server);
            if (!result.Folders.Contains(server.Folder, StringComparer.OrdinalIgnoreCase))
                result.Folders.Add(server.Folder);
        }

        private static string ResolveMRemoteValue(
            XElement node,
            string valueAttribute,
            string inheritAttribute,
            string inheritedValue,
            string fallback)
        {
            if (ParseBool((string?)node.Attribute(inheritAttribute)))
                return inheritedValue;
            return (string?)node.Attribute(valueAttribute) ?? fallback;
        }

        private static bool TryDecryptMRemoteValue(string encryptedValue, string password, int iterations, out string plaintext)
        {
            plaintext = string.Empty;
            try
            {
                var bytes = Convert.FromBase64String(encryptedValue);
                if (bytes.Length < 49)
                    return false;

                var salt = bytes.AsSpan(0, 16).ToArray();
                var nonce = bytes.AsSpan(16, 16).ToArray();
                var cipherLength = bytes.Length - 48;
                var cipher = bytes.AsSpan(32, cipherLength).ToArray();
                var tag = bytes.AsSpan(bytes.Length - 16, 16).ToArray();
                var key = Rfc2898DeriveBytes.Pbkdf2(password, salt, iterations, HashAlgorithmName.SHA1, 32);
                try
                {
                    if (!TryDecryptAesGcm(cipher, nonce, tag, key, out var plainBytes, salt))
                        return false;
                    plaintext = Encoding.UTF8.GetString(plainBytes);
                    CryptographicOperations.ZeroMemory(plainBytes);
                    return true;
                }
                finally
                {
                    CryptographicOperations.ZeroMemory(key);
                }
            }
            catch (Exception ex) when (ex is FormatException or CryptographicException or ArgumentException)
            {
                return false;
            }
        }

        private static async Task<XDocument> LoadSafeXmlAsync(string path, CancellationToken cancellationToken)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(path);
            var info = new FileInfo(path);
            if (!info.Exists)
                throw new FileNotFoundException("Connection file not found.", path);
            if (info.Length > MaxImportBytes)
                throw new InvalidDataException("The selected connection file is too large.");

            var settings = new XmlReaderSettings
            {
                Async = true,
                DtdProcessing = DtdProcessing.Prohibit,
                XmlResolver = null,
                MaxCharactersInDocument = MaxImportBytes,
                IgnoreComments = true
            };
            await using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read, 81920, useAsync: true);
            using var reader = XmlReader.Create(stream, settings);
            return await XDocument.LoadAsync(reader, LoadOptions.None, cancellationToken).ConfigureAwait(false);
        }

        private static (byte[] CipherText, byte[] Nonce, byte[] Tag) EncryptAesGcm(byte[] plaintext, byte[] key)
        {
            var nonce = RandomNumberGenerator.GetBytes(12);
            var cipher = new byte[plaintext.Length];
            var tag = new byte[16];
            using var aes = new AesGcm(key, 16);
            aes.Encrypt(nonce, plaintext, cipher, tag);
            return (cipher, nonce, tag);
        }

        private static bool TryDecryptAesGcm(byte[] cipher, byte[] nonce, byte[] tag, byte[] key, out byte[] plaintext, byte[]? associatedData = null)
        {
            if (nonce.Length != 12)
                return Netvium.Helpers.FlexibleAesGcm.TryDecrypt(key, nonce, cipher, tag, associatedData ?? Array.Empty<byte>(), out plaintext);

            plaintext = new byte[cipher.Length];
            try
            {
                using var aes = new AesGcm(key, 16);
                aes.Decrypt(nonce, cipher, tag, plaintext);
                return true;
            }
            catch (Exception ex) when (ex is CryptographicException or ArgumentException)
            {
                CryptographicOperations.ZeroMemory(plaintext);
                plaintext = Array.Empty<byte>();
                return false;
            }
        }

        private static byte[]? ReadBase64Attribute(XElement element, string name)
        {
            var value = (string?)element.Attribute(name);
            if (string.IsNullOrWhiteSpace(value))
                return null;
            try { return Convert.FromBase64String(value); }
            catch (FormatException) { return null; }
        }

        private static int ParseInt(string? value, int fallback) =>
            int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsed) ? parsed : fallback;

        private static bool ParseBool(string? value) =>
            bool.TryParse(value, out var parsed) && parsed;

        private static int NormalizePort(int port) => port is > 0 and <= 65535 ? port : 3389;

        public static string GetEndpointKey(string? host, int port) =>
            $"{(host ?? string.Empty).Trim().ToLowerInvariant()}:{NormalizePort(port)}";

        private static string BuildFolderPath(IEnumerable<string> folders) =>
            string.Join(" / ", folders.Where(f => !string.IsNullOrWhiteSpace(f)).Select(f => f.Trim()));

        private static ConnectionTransferImportResult PasswordNeeded(ConnectionTransferFileKind kind, string? error = null) =>
            new() { FileKind = kind, PasswordRequired = true, ErrorMessage = error };

        private static ConnectionTransferImportResult Failure(ConnectionTransferFileKind kind, string error) =>
            new() { FileKind = kind, ErrorMessage = error };

        private sealed record MRemoteInheritedValues(
            string Protocol,
            string Username,
            string Domain,
            string EncryptedPassword,
            int Port);
    }
}
