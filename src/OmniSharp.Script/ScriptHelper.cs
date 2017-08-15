using System;
using System.Collections.Generic;
using System.Reflection;
using Dotnet.Script.NuGetMetadataResolver;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Scripting;
using Microsoft.CodeAnalysis.Scripting.Hosting;
using Microsoft.Extensions.Configuration;

namespace OmniSharp.Script
{
    public class ScriptHelper
    {
        private readonly IConfiguration _configuration;

        // aligned with CSI.exe
        // https://github.com/dotnet/roslyn/blob/version-2.0.0-rc3/src/Interactive/csi/csi.rsp
        internal static readonly IEnumerable<string> DefaultNamespaces = new[]
        {
            "System",
            "System.IO",
            "System.Collections.Generic",
            "System.Diagnostics",
            "System.Linq",
            "System.Text",
            "System.Threading.Tasks",
            "Microsoft.Extensions.Logging",
            "Microsoft.Azure.WebJobs",
            "Microsoft.Azure.WebJobs.Host",
            "System.Net.Http"
        };

        private static readonly CSharpParseOptions ParseOptions = new CSharpParseOptions(LanguageVersion.Default, DocumentationMode.Parse, SourceCodeKind.Script);

        private readonly Lazy<CSharpCompilationOptions> _compilationOptions;

        public static string FunctionsAssembliesPath => Environment.ExpandEnvironmentVariables("%appdata%\\npm\\node_modules\\azure-functions-core-tools\\bin");

        public ScriptHelper(IConfiguration configuration = null)
        {
            this._configuration = configuration;
            _compilationOptions = new Lazy<CSharpCompilationOptions>(CreateCompilationOptions);
        }

        private CSharpCompilationOptions CreateCompilationOptions()
        {
            var compilationOptions = new CSharpCompilationOptions(
                OutputKind.DynamicallyLinkedLibrary,
                usings: DefaultNamespaces,
                allowUnsafe: true,
                metadataReferenceResolver:
                CreateMetadataReferenceResolver(),
                sourceReferenceResolver: ScriptSourceResolver.Default,
                assemblyIdentityComparer: DesktopAssemblyIdentityComparer.Default).WithSpecificDiagnosticOptions(
                new Dictionary<string, ReportDiagnostic>
                {
                    // ensure that specific warnings about assembly references are always suppressed
                    // https://github.com/dotnet/roslyn/issues/5501
                    {"CS1701", ReportDiagnostic.Suppress},
                    {"CS1702", ReportDiagnostic.Suppress},
                    {"CS1705", ReportDiagnostic.Suppress}
                });

            var topLevelBinderFlagsProperty =
                typeof(CSharpCompilationOptions).GetProperty("TopLevelBinderFlags",
                    BindingFlags.Instance | BindingFlags.NonPublic);
            var binderFlagsType = typeof(CSharpCompilationOptions).GetTypeInfo().Assembly
                .GetType("Microsoft.CodeAnalysis.CSharp.BinderFlags");

            var ignoreCorLibraryDuplicatedTypesMember =
                binderFlagsType?.GetField("IgnoreCorLibraryDuplicatedTypes", BindingFlags.Static | BindingFlags.Public);
            var ignoreCorLibraryDuplicatedTypesValue = ignoreCorLibraryDuplicatedTypesMember?.GetValue(null);
            if (ignoreCorLibraryDuplicatedTypesValue != null)
            {
                topLevelBinderFlagsProperty?.SetValue(compilationOptions, ignoreCorLibraryDuplicatedTypesValue);
            }

            return compilationOptions;
        }

        private CachingScriptMetadataResolver CreateMetadataReferenceResolver()
        {
            bool enableScriptNuGetReferences = false;

            if (_configuration != null)
            {
                if (!bool.TryParse(_configuration["enableScriptNuGetReferences"], out enableScriptNuGetReferences))
                {
                    enableScriptNuGetReferences = false;
                }
            }

            var resolver = ScriptMetadataResolver.Default.WithSearchPaths(FunctionsAssembliesPath);
            return enableScriptNuGetReferences ? new CachingScriptMetadataResolver(new NuGetMetadataReferenceResolver(resolver)) 
                : new CachingScriptMetadataResolver(resolver);
        }
 
        public ProjectInfo CreateProject(string csxFileName, IEnumerable<MetadataReference> references, IEnumerable<string> namespaces = null)
        {
            var project = ProjectInfo.Create(
                id: ProjectId.CreateNewId(),
                version: VersionStamp.Create(),
                name: csxFileName,
                assemblyName: $"{csxFileName}.dll",
                language: LanguageNames.CSharp,
                compilationOptions: namespaces == null ? _compilationOptions.Value : _compilationOptions.Value.WithUsings(namespaces),
                metadataReferences: references,
                parseOptions: ParseOptions,
                isSubmission: true,
                hostObjectType: typeof(CommandLineScriptGlobals));

            return project;
        }
    }
}
