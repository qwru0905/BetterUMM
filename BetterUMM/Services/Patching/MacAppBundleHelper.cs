using System;
using System.IO;
using System.Linq;
using System.Text;
using System.Xml.Linq;

namespace BetterUMM.Services.Patching
{
    public static class MacAppBundleHelper
    {
        private const string BinaryPlistMagic = "bplist";

        public static string ResolveExecutablePath(string appBundlePath)
        {
            string infoPlistPath = Path.Combine(appBundlePath, "Contents", "Info.plist");
            if (!File.Exists(infoPlistPath))
                throw new FileNotFoundException($"Info.plist을 찾을 수 없습니다: {infoPlistPath}", infoPlistPath);

            byte[] header = new byte[6];
            using (var headerStream = File.OpenRead(infoPlistPath))
            {
                int read = headerStream.Read(header, 0, header.Length);
                if (read == header.Length && Encoding.ASCII.GetString(header) == BinaryPlistMagic)
                    throw new NotSupportedException($"바이너리 plist 형식은 지원하지 않습니다: {infoPlistPath}");
            }

            XDocument document = XDocument.Load(infoPlistPath);
            XElement? dict = document.Root?.Element("dict");
            if (dict == null)
                throw new InvalidDataException($"Info.plist에서 <dict>를 찾을 수 없습니다: {infoPlistPath}");

            string? executableName = null;
            var children = dict.Elements().ToArray();
            for (int i = 0; i < children.Length - 1; i++)
            {
                if (children[i].Name == "key" && children[i].Value == "CFBundleExecutable")
                {
                    executableName = children[i + 1].Value;
                    break;
                }
            }

            if (string.IsNullOrEmpty(executableName))
                throw new InvalidDataException($"Info.plist에 CFBundleExecutable 키가 없습니다: {infoPlistPath}");

            return Path.Combine(appBundlePath, "Contents", "MacOS", executableName);
        }
    }
}
