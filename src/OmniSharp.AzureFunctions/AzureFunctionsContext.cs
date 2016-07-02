using Microsoft.CodeAnalysis;
using System.Collections.Generic;
using System.Composition;

namespace OmniSharp.AzureFunctions
{
    [Export, Shared]
    public class AzureFunctionsContext
    {
        public HashSet<string> CsxFilesBeingProcessed { get; } = new HashSet<string>();

        // All of the followings are keyed with the file path
        // Each .csx file is wrapped into a project
        public Dictionary<string, ProjectInfo> CsxFileProjects { get; } = new Dictionary<string, ProjectInfo>();

        public Dictionary<string, List<MetadataReference>> CsxReferences { get; } = new Dictionary<string, List<MetadataReference>>();

        public Dictionary<string, List<ProjectInfo>> CsxLoadReferences { get; } = new Dictionary<string, List<ProjectInfo>>();

        public Dictionary<string, List<string>> CsxUsings { get; } = new Dictionary<string, List<string>>();

        
        public string RootPath { get; set; }
    }
}
