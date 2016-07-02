using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Scripting;

namespace OmniSharp.AzureFunctions
{
    public class FunctionMetadataResolver : MetadataReferenceResolver
    {
        private readonly string _id = Guid.NewGuid().ToString();
        private readonly string _privateAssembliesPath;
        private readonly string[] _assemblyExtensions = new[] { ".exe", ".dll" };
        private readonly ScriptMetadataResolver _scriptResolver;
        private readonly string _functionDirectory;

        public FunctionMetadataResolver(string functionDirectory)
        {
            _functionDirectory = functionDirectory;
            _privateAssembliesPath = Path.Combine(Path.GetFullPath(functionDirectory), "bin");
            _scriptResolver = ScriptMetadataResolver.Default.WithSearchPaths(_privateAssembliesPath);
        }

        public override bool Equals(object other)
        {
            var otherResolver = other as FunctionMetadataResolver;
            return otherResolver != null && string.Compare(_id, otherResolver._id, StringComparison.Ordinal) == 0;
        }

        public override int GetHashCode()
        {
            return _id.GetHashCode();
        }

        public override ImmutableArray<PortableExecutableReference> ResolveReference(string reference, string baseFilePath, MetadataReferenceProperties properties)
        {
            if (string.IsNullOrEmpty(reference))
            {
                return ImmutableArray<PortableExecutableReference>.Empty;
            }

            if (!HasValidAssemblyFileExtension(reference))
            {
                // Try to resolve using the default resolver (framework assemblies, e.g. System.Core, System.Xml, etc.)
                ImmutableArray<PortableExecutableReference> result = _scriptResolver.ResolveReference(reference, baseFilePath, properties);

                // If the default script resolver can't resolve the assembly
                // check if this is one of host's shared assemblies
                if (result.IsEmpty)
                {
                    Assembly assembly = null;

                    if (TryResolveSharedAssembly(reference, out assembly))
                    {
                        result = ImmutableArray.Create(MetadataReference.CreateFromFile(assembly.Location));
                    }
                }

                return result;
            }

            return GetMetadataFromReferencePath(reference);
        }

        private bool TryResolveSharedAssembly(string reference, out Assembly assembly)
        {
            assembly = null;

            string sharedAssembliesPath = Path.Combine("%USERPROFILE%", ".azurefunctionsassemblies", $"{reference}.dll");
            if (File.Exists(sharedAssembliesPath))
            {
                assembly = Assembly.LoadFile(sharedAssembliesPath);
            }

            return assembly != null;
        }

        private bool HasValidAssemblyFileExtension(string reference)
        {
            return _assemblyExtensions.Contains(Path.GetExtension(reference));
        }

        private ImmutableArray<PortableExecutableReference> GetMetadataFromReferencePath(string reference)
        {
            if (Path.IsPathRooted(reference))
            {
                // If the path is rooted, create a direct reference to the assembly file
                return ImmutableArray.Create(MetadataReference.CreateFromFile(reference));
            }
            else
            {
                string filePath = Path.GetFullPath(Path.Combine(_privateAssembliesPath, reference));
                if (File.Exists(filePath))
                {
                    return ImmutableArray.Create(MetadataReference.CreateFromFile(filePath));
                }
            }

            return ImmutableArray<PortableExecutableReference>.Empty;
        }
    }
}
