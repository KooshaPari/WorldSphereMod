using System;
using System.IO;
using System.Reflection;
using System.Security.Cryptography;

namespace WorldSphereMod.Bridge
{
    public static class RuntimeIdentity
    {
        public sealed class AssemblyIdentity
        {
            public string fullName = "";
            public string loadedModuleVersionId = "";
            public string? location;
            public string? diskSha256;
            public string? diskReadError;
            public string hashScope = "disk-at-query-time; loadedModuleVersionId identifies the loaded module";
        }

        public static string HashFile(string path)
        {
            using (var stream = File.OpenRead(path))
            using (var hash = SHA256.Create())
                return BitConverter.ToString(hash.ComputeHash(stream)).Replace("-", "");
        }

        public static AssemblyIdentity Describe(Assembly assembly)
        {
            var result = new AssemblyIdentity { fullName = assembly.FullName ?? "", loadedModuleVersionId = assembly.ManifestModule.ModuleVersionId.ToString("D") };
            try
            {
                result.location = assembly.Location;
                if (string.IsNullOrEmpty(result.location)) result.diskReadError = "dynamic_or_locationless_assembly";
                else result.diskSha256 = HashFile(result.location);
            }
            catch (Exception ex) { result.diskReadError = ex.GetType().Name + ": " + ex.Message; }
            return result;
        }
    }
}
