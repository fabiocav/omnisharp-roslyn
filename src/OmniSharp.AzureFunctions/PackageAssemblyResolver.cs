using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace OmniSharp.AzureFunctions
{
    // Copyright (c) .NET Foundation. All rights reserved.
    // Licensed under the MIT License. See License.txt in the project root for license information.

    using System;
    using System.Collections.Generic;
    using System.Collections.Immutable;
    using System.Globalization;
    using System.IO;
    using System.Linq;
    using System.Reflection;
    using Newtonsoft.Json.Linq;

    namespace Microsoft.Azure.WebJobs.Script.Description
    {
        /// <summary>
        /// Resolves assemblies referenced by NuGet packages used by the function.
        /// </summary>
        public sealed class PackageAssemblyResolver
        {
            private const string EmptyFolderFileMarker = "_._";
            private const string FrameworkTargetName = ".NETFramework,Version=v4.6";

            private readonly ImmutableArray<PackageReference> _packages;

            public PackageAssemblyResolver(string functionDirectory)
            {
                _packages = InitializeAssemblyRegistry(functionDirectory);
            }

            public ImmutableArray<PackageReference> Packages
            {
                get
                {
                    return _packages;
                }
            }

            public IEnumerable<string> AssemblyReferences
            {
                get
                {
                    return _packages.Aggregate(new List<string>(), (assemblies, p) =>
                    {
                        assemblies.AddRange(p.Assemblies.Values);
                        assemblies.AddRange(p.FrameworkAssemblies.Values);
                        return assemblies;
                    });
                }
            }

            private static ImmutableArray<PackageReference> InitializeAssemblyRegistry(string functionDirectory)
            {
                var builder = ImmutableArray<PackageReference>.Empty.ToBuilder();
                string fileName = Path.Combine(functionDirectory, "project.lock.json");

                if (File.Exists(fileName))
                {
                    var jobject = JObject.Parse(File.ReadAllText(fileName));

                    var target = jobject.SelectTokens(string.Format(CultureInfo.InvariantCulture, "$.targets['{0}']", FrameworkTargetName)).FirstOrDefault();

                    if (target != null)
                    {
                        string nugetHome = PackageManager.GetNugetPackagesPath();

                        foreach (JProperty token in target)
                        {
                            var referenceNameParts = token.Name.Split('/');
                            if (referenceNameParts.Length != 2)
                            {
                                throw new FormatException(string.Format(CultureInfo.InvariantCulture, "The package name '{0}' is not correctly formatted.", token.Name));
                            }

                            var package = new PackageReference(referenceNameParts[0], referenceNameParts[1]);

                            var references = token.SelectTokens("$..compile").FirstOrDefault();
                            if (references != null)
                            {
                                foreach (JProperty reference in references)
                                {
                                    if (!reference.Name.EndsWith(EmptyFolderFileMarker, StringComparison.Ordinal))
                                    {
                                        string path = Path.Combine(nugetHome, token.Name, reference.Name);
                                        path = path.Replace('/', '\\');

                                        if (File.Exists(path))
                                        {
                                            package.Assemblies.Add(AssemblyName.GetAssemblyName(path), path);
                                        }
                                    }
                                }
                            }

                            var frameworkAssemblies = token.SelectTokens("$..frameworkAssemblies").FirstOrDefault();
                            if (frameworkAssemblies != null)
                            {
                                foreach (var assembly in frameworkAssemblies)
                                {
                                    string assemblyName = assembly.ToString();
                                    package.FrameworkAssemblies.Add(new AssemblyName(assemblyName), assemblyName);
                                }
                            }

                            builder.Add(package);
                        }
                    }
                }

                return builder.ToImmutableArray();
            }

            public bool TryResolveAssembly(string name, out string path)
            {
                path = null;
                var assemblyName = new AssemblyName(name);

                foreach (var package in _packages)
                {
                    if (package.Assemblies.TryGetValue(assemblyName, out path) ||
                        package.FrameworkAssemblies.TryGetValue(assemblyName, out path))
                    {
                        break;
                    }
                }

                return path != null;
            }
        }
    }

    public class PackageReference
    {
        public PackageReference(string name, string version)
        {
            Name = name;
            Version = version;
            Assemblies = new Dictionary<AssemblyName, string>(new AssemblyNameEqualityComparer());
            FrameworkAssemblies = new Dictionary<AssemblyName, string>(new AssemblyNameEqualityComparer());
        }

        public Dictionary<AssemblyName, string> Assemblies { get; private set; }

        public Dictionary<AssemblyName, string> FrameworkAssemblies { get; private set; }

        public string Name { get; private set; }

        public string Version { get; private set; }

        private class AssemblyNameEqualityComparer : IEqualityComparer<AssemblyName>
        {
            public bool Equals(AssemblyName x, AssemblyName y)
            {
                if (x == y)
                {
                    return true;
                }

                if (x == null || y == null)
                {
                    return false;
                }

                return string.Compare(x.FullName, y.FullName, StringComparison.OrdinalIgnoreCase) == 0;
            }

            public int GetHashCode(AssemblyName obj)
            {
                if (obj == null || obj.FullName == null)
                {
                    return 0;
                }

                return obj.FullName.GetHashCode();
            }
        }
    }
}
