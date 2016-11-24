using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Composition;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Scripting;
using Microsoft.CodeAnalysis.Scripting;
using Microsoft.CodeAnalysis.Text;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using OmniSharp.AzureFunctions.Microsoft.Azure.WebJobs.Script.Description;
using OmniSharp.Models.v1;
using OmniSharp.Services;


namespace OmniSharp.AzureFunctions
{
    [Export(typeof(IProjectSystem))]
    public class AzureFunctionsProjectSystem : IProjectSystem
    {
        private static readonly string BaseAssemblyPath = Path.GetDirectoryName(typeof(object).Assembly.Location);

        private static readonly string[] DefaultAssemblyReferences =
         {
                "System",
                "System.Core",
                "System.Configuration",
                "System.Xml",
                "System.Net.Http",
                "Microsoft.CSharp"
            };

        private static readonly string[] DefaultNamespaceImports =
            {
                "System",
                "System.Collections.Generic",
                "System.IO",
                "System.Linq",
                "System.Net.Http",
                "System.Threading.Tasks",
                "Microsoft.Azure.WebJobs",
                "Microsoft.Azure.WebJobs.Host"
            };

        [ImportingConstructor]
        public AzureFunctionsProjectSystem(OmnisharpWorkspace workspace, IOmnisharpEnvironment env, ILoggerFactory loggerFactory, AzureFunctionsContext scriptCsContext)
        {
            Workspace = workspace;
            Environment = env;
            Context = scriptCsContext;
            Logger = loggerFactory.CreateLogger<AzureFunctionsProjectSystem>();

            Workspace.WorkspaceChanged += Workspace_WorkspaceChanged;
        }

        CSharpParseOptions ScriptParseOptions { get; } = new CSharpParseOptions(LanguageVersion.CSharp6, DocumentationMode.Parse, SourceCodeKind.Script);

        OmnisharpWorkspace Workspace { get; }

        IOmnisharpEnvironment Environment { get; }

        AzureFunctionsContext Context { get; }

        ILogger Logger { get; }

        public string Key { get { return "Azure Functions"; } }

        public string Language { get { return LanguageNames.CSharp; } }

        public IEnumerable<string> Extensions { get; } = new[] { ".csx" };

        public void Initalize(IConfiguration configuration)
        {
            Logger.LogInformation($"Locating function files in '{Environment.Path}'.");

            foreach (var directory in Directory.GetDirectories(Environment.Path))
            {
                string scriptFile = Path.Combine(directory, "run.csx");
                if (File.Exists(scriptFile))
                {
                    try
                    {
                        CreateFunctionProject(scriptFile);
                    }
                    catch (Exception ex)
                    {
                        Logger.LogError($"Function {Path.GetDirectoryName(directory)} will be ignored due to the following error:", ex.ToString());
                        Logger.LogError(ex.ToString());
                        Logger.LogError(ex.InnerException?.ToString() ?? "No inner exception.");
                    }
                }
            }

            Context.RootPath = Environment.Path;
        }

        private void CreateFunctionProject(string functionScriptFile)
        {
            var compilationOptions = new CSharpCompilationOptions(
                outputKind: OutputKind.DynamicallyLinkedLibrary,
                usings: DefaultNamespaceImports);

            List<MetadataReference> references = ResolveReferences(functionScriptFile);

            ScriptOptions scriptOptions = ScriptOptions.Default
                .WithMetadataResolver(new FunctionMetadataResolver(Path.GetDirectoryName(functionScriptFile)))
                .WithReferences(references)
                .WithImports(DefaultNamespaceImports)
                .WithFilePath(functionScriptFile)
                .WithSourceResolver(new SourceFileResolver(ImmutableArray<string>.Empty, Path.GetDirectoryName(functionScriptFile)));


            Script<object> script = CSharpScript.Create(File.ReadAllText(functionScriptFile), scriptOptions, null);
            var compilation = script.GetCompilation();

            var project = ProjectInfo.Create(
                         id: ProjectId.CreateNewId(Guid.NewGuid().ToString()),
                         version: VersionStamp.Create(),
                         name: Directory.GetParent(functionScriptFile).Name,
                         filePath: Path.Combine(Path.GetDirectoryName(functionScriptFile), "function.json"),
                         assemblyName: $"{Directory.GetParent(functionScriptFile).Name}.dll",
                         language: LanguageNames.CSharp,
                         compilationOptions: compilation.Options,
                         parseOptions: ScriptParseOptions,
                         metadataReferences: compilation.References,
                         isSubmission: true);

            Workspace.AddProject(project);

            AddFile(functionScriptFile, project);
        }

        private List<MetadataReference> ResolveReferences(string functionScriptFile)
        {
            List<MetadataReference> references = new List<MetadataReference>();

            string frameworkPath = Path.GetDirectoryName(typeof(object).Assembly.Location);

            var packageManager = new PackageAssemblyResolver(Path.GetDirectoryName(functionScriptFile));

            foreach (var assemblyReference in packageManager.AssemblyReferences.Union(DefaultAssemblyReferences))
            {
                var path = assemblyReference;
                if (!Path.IsPathRooted(path))
                {
                    path = Path.Combine(frameworkPath, assemblyReference + ".dll");
                }

                if (File.Exists(path))
                {
                    references.Add(MetadataReference.CreateFromFile(path));
                }
            }

            // mscorlib
            references.Add(MetadataReference.CreateFromFile(typeof(object).Assembly.Location));

            // Default references
            string scriptReferencesPath = Path.Combine(Path.GetDirectoryName(this.GetType().Assembly.Location), "scriptreferences");

            if (Directory.Exists(scriptReferencesPath))
            {
                references.AddRange(Directory.GetFiles(scriptReferencesPath, "*.dll").Select(a => MetadataReference.CreateFromFile(a)));
            }

            return references;
        }

        private void AddFile(string functionScriptFile, ProjectInfo project)
        {
            using (var stream = File.OpenRead(functionScriptFile))
            using (var reader = new StreamReader(stream))
            {
                var fileName = Path.GetFileName(functionScriptFile);
                var csxFile = reader.ReadToEnd();

                var documentId = DocumentId.CreateNewId(project.Id, fileName);
                var documentInfo = DocumentInfo.Create(documentId, fileName, null, SourceCodeKind.Script, null, functionScriptFile)
                    .WithSourceCodeKind(SourceCodeKind.Script)
                    .WithTextLoader(TextLoader.From(TextAndVersion.Create(SourceText.From(csxFile), VersionStamp.Create())));

                Workspace.AddDocument(documentInfo);
            }
        }

        private void Workspace_WorkspaceChanged(object sender, WorkspaceChangeEventArgs e)
        {
            Console.WriteLine(e.DocumentId);
        }

        public Task<object> GetWorkspaceModelAsync(WorkspaceInformationRequest request)
        {
            throw new NotImplementedException();
        }

        public Task<object> GetProjectModelAsync(string filePath)
        {
            throw new NotImplementedException();
        }
    }
}
