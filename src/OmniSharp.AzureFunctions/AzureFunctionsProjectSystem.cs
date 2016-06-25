using System;
using System.Collections.Generic;
using System.Composition;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Text;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using OmniSharp.Models.v1;
using OmniSharp.Services;
using System.Collections.Immutable;

namespace OmniSharp.AzureFunctions
{
    [Export(typeof(IProjectSystem))]
    public class AzureFunctionsProjectSystem : IProjectSystem
    {
        private static readonly string BaseAssemblyPath = Path.GetDirectoryName(typeof(object).Assembly.Location);
        
        [ImportingConstructor]
        public AzureFunctionsProjectSystem(OmnisharpWorkspace workspace, IOmnisharpEnvironment env, ILoggerFactory loggerFactory, AzureFunctionsContext scriptCsContext)
        {
            Workspace = workspace;
            Environment = env;
            Context = scriptCsContext;
            Logger = loggerFactory.CreateLogger<AzureFunctionsProjectSystem>();
        }

        CSharpParseOptions CsxParseOptions { get; } = new CSharpParseOptions(LanguageVersion.CSharp6, DocumentationMode.Parse, SourceCodeKind.Script);
        IEnumerable<MetadataReference> DotNetBaseReferences { get; } = new[]
            {
                MetadataReference.CreateFromFile(typeof(object).GetTypeInfo().Assembly.Location),        // mscorlib
                MetadataReference.CreateFromFile(typeof(Enumerable).GetTypeInfo().Assembly.Location),    // systemCore
            };

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
            

            // TODO: Add usings and references
            

            // TODO: Add metadata references
        }

        private void CreateFunctionProject(string functionScriptFile)
        {
            var compilationOptions = new CSharpCompilationOptions(
                outputKind: OutputKind.DynamicallyLinkedLibrary,
                usings: new[] { "System", "System.Linq", "System.Xml" });

            var project = ProjectInfo.Create(
                id: ProjectId.CreateNewId(Guid.NewGuid().ToString()),
                version: VersionStamp.Create(),
                name: Path.GetDirectoryName(functionScriptFile),
                assemblyName: $"{functionScriptFile}.dll",
                language: LanguageNames.CSharp,
                compilationOptions: compilationOptions,
                parseOptions: CsxParseOptions,
                metadataReferences: new[] { MetadataReference.CreateFromFile(typeof(object).Assembly.Location) },
                isSubmission: true);

            Workspace.AddProject(project);
        }
        ///// <summary>
        ///// Each .csx file is to be wrapped in its own project.
        ///// This recursive function does a depth first traversal of the .csx files, following #load references
        ///// </summary>
        //private ProjectInfo CreateCsxProject(string csxPath, IScriptServicesBuilder baseBuilder)
        //{
        //    // Circular #load chains are not allowed
        //    if (Context.CsxFilesBeingProcessed.Contains(csxPath))
        //    {
        //        throw new Exception($"Circular refrences among script files are not allowed: {csxPath} #loads files that end up trying to #load it again.");
        //    }

        //    // If we already have a project for this path just use that
        //    if (Context.CsxFileProjects.ContainsKey(csxPath))
        //    {
        //        return Context.CsxFileProjects[csxPath];
        //    }

        //    // Process the file with ScriptCS first
        //    Logger.LogInformation($"Processing script {csxPath}...");
        //    Context.CsxFilesBeingProcessed.Add(csxPath);
        //    var localScriptServices = baseBuilder.ScriptName(csxPath).Build();
        //    var processResult = localScriptServices.FilePreProcessor.ProcessFile(csxPath);

        //    // CSX file usings
        //    Context.CsxUsings[csxPath] = processResult.Namespaces.ToList();

        //    var compilationOptions = new CSharpCompilationOptions(
        //        outputKind: OutputKind.DynamicallyLinkedLibrary,
        //        usings: Context.CommonUsings.Union(Context.CsxUsings[csxPath]));

        //    // #r refernces
        //    Context.CsxReferences[csxPath] = localScriptServices.MakeMetadataReferences(processResult.References).ToList();

        //    //#load references recursively
        //    Context.CsxLoadReferences[csxPath] =
        //        processResult
        //            .LoadedScripts
        //            .Distinct()
        //            .Except(new[] { csxPath })
        //            .Select(loadedCsxPath => CreateCsxProject(loadedCsxPath, baseBuilder))
        //            .ToList();

        //    // Create the wrapper project and add it to the workspace
        //    Logger.LogDebug($"Creating project for script {csxPath}.");
        //    var csxFileName = Path.GetFileName(csxPath);
        //    var project = ProjectInfo.Create(
        //        id: ProjectId.CreateNewId(Guid.NewGuid().ToString()),
        //        version: VersionStamp.Create(),
        //        name: csxFileName,
        //        assemblyName: $"{csxFileName}.dll",
        //        language: LanguageNames.CSharp,
        //        compilationOptions: compilationOptions,
        //        parseOptions: CsxParseOptions,
        //        metadataReferences: Context.CommonReferences.Union(Context.CsxReferences[csxPath]),
        //        projectReferences: Context.CsxLoadReferences[csxPath].Select(p => new ProjectReference(p.Id)),
        //        isSubmission: true,
        //        hostObjectType: typeof(IScriptHost));

        //    Workspace.AddProject(project);

        //    AddFile(csxPath, project.Id);

        //    //----------LOG ONLY------------
        //    Logger.LogDebug($"All references by {csxFileName}: \n{string.Join("\n", project.MetadataReferences.Select(r => r.Display))}");
        //    Logger.LogDebug($"All #load projects by {csxFileName}: \n{string.Join("\n", Context.CsxLoadReferences[csxPath].Select(p => p.Name))}");
        //    Logger.LogDebug($"All usings in {csxFileName}: \n{string.Join("\n", (project.CompilationOptions as CSharpCompilationOptions)?.Usings ?? new ImmutableArray<string>())}");
        //    //------------------------------

        //    // Traversal administration
        //    Context.CsxFileProjects[csxPath] = project;
        //    Context.CsxFilesBeingProcessed.Remove(csxPath);

        //    return project;
        //}


        private void AddFile(string filePath, ProjectId projectId)
        {
            using (var stream = File.OpenRead(filePath))
            using (var reader = new StreamReader(stream))
            {
                var fileName = Path.GetFileName(filePath);
                var csxFile = reader.ReadToEnd();

                var documentId = DocumentId.CreateNewId(projectId, fileName);
                var documentInfo = DocumentInfo.Create(documentId, fileName, null, SourceCodeKind.Script, null, filePath)
                    .WithSourceCodeKind(SourceCodeKind.Script)
                    .WithTextLoader(TextLoader.From(TextAndVersion.Create(SourceText.From(csxFile), VersionStamp.Create())));
                Workspace.AddDocument(documentInfo);
            }
        }

        Task<object> IProjectSystem.GetProjectModel(string path)
        {
            return Task.FromResult<object>(null);
        }

        Task<object> IProjectSystem.GetInformationModel(WorkspaceInformationRequest request)
        {
            return Task.FromResult<object>(new AzureFunctionsContextModel(Context));
        }
    }
}
