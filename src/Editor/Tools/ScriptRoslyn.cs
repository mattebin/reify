using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using Newtonsoft.Json.Linq;
using UnityEditor;
using UnityEditor.Compilation;

namespace Reify.Editor.Tools
{
    /// <summary>
    /// Roslyn-backed script inspection without a hard package dependency.
    /// Unity ships Roslyn assemblies in modern editors, but reify keeps the
    /// package installable by resolving them via reflection at runtime.
    /// </summary>
    internal static class ScriptRoslyn
    {
        public static object Inspect(string assetPath)
        {
            ScriptEvidence.ValidateAssetPath(assetPath);

            var abs = ScriptEvidence.AbsolutePath(assetPath);
            if (!File.Exists(abs))
                throw new InvalidOperationException($"Script file not found: {assetPath}");

            var text = File.ReadAllText(abs);
            var summary = JObject.FromObject(ScriptEvidence.Summarize(assetPath, text));
            var roslyn = RoslynSession.Create();

            var diagnostics = roslyn.GetDiagnostics(assetPath, abs, text, out var inspectionMode, out var warnings, out var syntaxOk);
            var classes = roslyn.GetTypeSummaries(abs, text);

            var mergedWarnings = new List<string>();
            foreach (var warning in summary["warnings"] as JArray ?? new JArray())
                mergedWarnings.Add(warning?.ToString());
            mergedWarnings.AddRange(warnings);

            return new
            {
                asset_path       = assetPath,
                guid             = summary.Value<string>("guid"),
                roslyn_available = true,
                inspection_mode  = inspectionMode,
                syntax_ok        = syntaxOk,
                compile_diagnostics = diagnostics.Select(d => d.ToEnvelope()).ToArray(),
                diagnostic_count = diagnostics.Count,
                class_count      = classes.Count,
                classes          = classes.ToArray(),
                script           = summary,
                warnings         = mergedWarnings
                    .Where(w => !string.IsNullOrWhiteSpace(w))
                    .Distinct(StringComparer.Ordinal)
                    .ToArray(),
                read_at_utc      = DateTime.UtcNow.ToString("o"),
                frame            = (long)UnityEngine.Time.frameCount
            };
        }

        private sealed class RoslynSession
        {
            private readonly Type _syntaxTreeType;
            private readonly Type _csharpSyntaxTreeType;
            private readonly Type _compilationType;
            private readonly Type _compilationOptionsType;
            private readonly Type _metadataReferenceType;
            private readonly Type _outputKindType;

            private RoslynSession(
                Type syntaxTreeType,
                Type csharpSyntaxTreeType,
                Type compilationType,
                Type compilationOptionsType,
                Type metadataReferenceType,
                Type outputKindType)
            {
                _syntaxTreeType = syntaxTreeType;
                _csharpSyntaxTreeType = csharpSyntaxTreeType;
                _compilationType = compilationType;
                _compilationOptionsType = compilationOptionsType;
                _metadataReferenceType = metadataReferenceType;
                _outputKindType = outputKindType;
            }

            public static RoslynSession Create()
            {
                var syntaxTreeType = FindType(
                    "Microsoft.CodeAnalysis.SyntaxTree, Microsoft.CodeAnalysis",
                    "Microsoft.CodeAnalysis.SyntaxTree");
                var csharpSyntaxTreeType = FindType(
                    "Microsoft.CodeAnalysis.CSharp.CSharpSyntaxTree, Microsoft.CodeAnalysis.CSharp",
                    "Microsoft.CodeAnalysis.CSharp.CSharpSyntaxTree");
                var compilationType = FindType(
                    "Microsoft.CodeAnalysis.CSharp.CSharpCompilation, Microsoft.CodeAnalysis.CSharp",
                    "Microsoft.CodeAnalysis.CSharp.CSharpCompilation");
                var compilationOptionsType = FindType(
                    "Microsoft.CodeAnalysis.CSharp.CSharpCompilationOptions, Microsoft.CodeAnalysis.CSharp",
                    "Microsoft.CodeAnalysis.CSharp.CSharpCompilationOptions");
                var metadataReferenceType = FindType(
                    "Microsoft.CodeAnalysis.MetadataReference, Microsoft.CodeAnalysis",
                    "Microsoft.CodeAnalysis.MetadataReference");
                var outputKindType = FindType(
                    "Microsoft.CodeAnalysis.OutputKind, Microsoft.CodeAnalysis",
                    "Microsoft.CodeAnalysis.OutputKind");

                if (syntaxTreeType == null || csharpSyntaxTreeType == null ||
                    compilationType == null || compilationOptionsType == null ||
                    metadataReferenceType == null || outputKindType == null)
                {
                    TryLoadRoslynAssemblies();
                    syntaxTreeType = FindType(
                        "Microsoft.CodeAnalysis.SyntaxTree, Microsoft.CodeAnalysis",
                        "Microsoft.CodeAnalysis.SyntaxTree");
                    csharpSyntaxTreeType = FindType(
                        "Microsoft.CodeAnalysis.CSharp.CSharpSyntaxTree, Microsoft.CodeAnalysis.CSharp",
                        "Microsoft.CodeAnalysis.CSharp.CSharpSyntaxTree");
                    compilationType = FindType(
                        "Microsoft.CodeAnalysis.CSharp.CSharpCompilation, Microsoft.CodeAnalysis.CSharp",
                        "Microsoft.CodeAnalysis.CSharp.CSharpCompilation");
                    compilationOptionsType = FindType(
                        "Microsoft.CodeAnalysis.CSharp.CSharpCompilationOptions, Microsoft.CodeAnalysis.CSharp",
                        "Microsoft.CodeAnalysis.CSharp.CSharpCompilationOptions");
                    metadataReferenceType = FindType(
                        "Microsoft.CodeAnalysis.MetadataReference, Microsoft.CodeAnalysis",
                        "Microsoft.CodeAnalysis.MetadataReference");
                    outputKindType = FindType(
                        "Microsoft.CodeAnalysis.OutputKind, Microsoft.CodeAnalysis",
                        "Microsoft.CodeAnalysis.OutputKind");
                }

                if (syntaxTreeType == null || csharpSyntaxTreeType == null ||
                    compilationType == null || compilationOptionsType == null ||
                    metadataReferenceType == null || outputKindType == null)
                {
                    throw new InvalidOperationException(
                        "Roslyn assemblies are not loaded in this Unity Editor. " +
                        "Expected Microsoft.CodeAnalysis + Microsoft.CodeAnalysis.CSharp.");
                }

                return new RoslynSession(
                    syntaxTreeType,
                    csharpSyntaxTreeType,
                    compilationType,
                    compilationOptionsType,
                    metadataReferenceType,
                    outputKindType);
            }

            private static void TryLoadRoslynAssemblies()
            {
                var editorDataPath = EditorApplication.applicationContentsPath;
                if (string.IsNullOrWhiteSpace(editorDataPath) || !Directory.Exists(editorDataPath))
                    return;

                var probeDirs = new[]
                {
                    Path.Combine(editorDataPath, "DotNetSdkRoslyn"),
                    Path.Combine(editorDataPath, "MonoBleedingEdge", "lib", "mono", "4.5"),
                    Path.Combine(editorDataPath, "MonoBleedingEdge", "lib", "mono", "msbuild", "Current", "bin", "Roslyn"),
                    Path.Combine(editorDataPath, "Tools", "BuildPipeline", "Compilation", "ApiUpdater")
                };

                var dlls = new[]
                {
                    "System.Collections.Immutable.dll",
                    "System.Memory.dll",
                    "System.Buffers.dll",
                    "System.Runtime.CompilerServices.Unsafe.dll",
                    "System.Reflection.Metadata.dll",
                    "System.Threading.Tasks.Extensions.dll",
                    "Microsoft.CodeAnalysis.dll",
                    "Microsoft.CodeAnalysis.CSharp.dll"
                };

                foreach (var probeDir in probeDirs)
                {
                    if (!Directory.Exists(probeDir)) continue;
                    foreach (var dll in dlls)
                    {
                        var path = Path.Combine(probeDir, dll);
                        if (!File.Exists(path)) continue;

                        var simpleName = Path.GetFileNameWithoutExtension(path);
                        if (AppDomain.CurrentDomain.GetAssemblies().Any(a =>
                        {
                            try { return string.Equals(a.GetName().Name, simpleName, StringComparison.OrdinalIgnoreCase); }
                            catch { return false; }
                        }))
                        {
                            continue;
                        }

                        try { System.Reflection.Assembly.LoadFrom(path); }
                        catch { /* keep probing */ }
                    }
                }
            }

            public List<DiagnosticEnvelope> GetDiagnostics(
                string assetPath,
                string absolutePath,
                string text,
                out string inspectionMode,
                out List<string> warnings,
                out bool syntaxOk)
            {
                warnings = new List<string>();
                var targetTree = ParseText(text, absolutePath);
                var syntaxDiagnostics = GetSyntaxDiagnostics(targetTree, text);
                syntaxOk = !syntaxDiagnostics.Any(d =>
                    string.Equals(d.Severity, "error", StringComparison.OrdinalIgnoreCase));

                var semanticDiagnostics = TryGetSemanticDiagnostics(assetPath, absolutePath, targetTree, warnings);
                if (semanticDiagnostics != null)
                {
                    inspectionMode = "syntax_and_semantic";
                    return semanticDiagnostics;
                }

                inspectionMode = "syntax_only";
                return syntaxDiagnostics;
            }

            public List<object> GetTypeSummaries(string absolutePath, string text)
            {
                var tree = ParseText(text, absolutePath);
                var root = InvokeMethod(tree, "GetRoot");
                var classes = new List<object>();
                CollectTypeSummaries(root, text, null, classes);
                return classes;
            }

            private object ParseText(string text, string absolutePath)
            {
                var parseText = _csharpSyntaxTreeType
                    .GetMethods(BindingFlags.Public | BindingFlags.Static)
                    .FirstOrDefault(m =>
                    {
                        if (m.Name != "ParseText") return false;
                        var ps = m.GetParameters();
                        return ps.Length > 0 && ps[0].ParameterType == typeof(string);
                    });

                if (parseText == null)
                    throw new InvalidOperationException("Could not find Roslyn CSharpSyntaxTree.ParseText(string, ...).");

                var args = BindArguments(parseText.GetParameters(), text, absolutePath);
                return parseText.Invoke(null, args);
            }

            private List<DiagnosticEnvelope> TryGetSemanticDiagnostics(
                string assetPath,
                string absolutePath,
                object targetTree,
                List<string> warnings)
            {
                var unityAssembly = FindUnityCompilationAssembly(absolutePath);
                if (unityAssembly == null)
                {
                    warnings.Add("Could not map the script to a Unity compilation assembly; semantic diagnostics fell back to syntax-only.");
                    return null;
                }

                var syntaxTrees = (IList)Activator.CreateInstance(typeof(List<>).MakeGenericType(_syntaxTreeType));
                var targetNormalized = NormalizePath(absolutePath);

                foreach (var sourceFile in unityAssembly.sourceFiles ?? Array.Empty<string>())
                {
                    var sourceAbs = ToAbsoluteProjectPath(sourceFile);
                    if (!File.Exists(sourceAbs)) continue;

                    var sourceText = File.ReadAllText(sourceAbs);
                    var tree = NormalizePath(sourceAbs) == targetNormalized
                        ? targetTree
                        : ParseText(sourceText, sourceAbs);
                    syntaxTrees.Add(tree);
                }

                if (syntaxTrees.Count == 0)
                {
                    warnings.Add($"Unity reported assembly '{unityAssembly.name}', but no readable source files were found; semantic diagnostics fell back to syntax-only.");
                    return null;
                }

                var references = (IList)Activator.CreateInstance(typeof(List<>).MakeGenericType(_metadataReferenceType));
                foreach (var referencePath in CollectReferencePaths(unityAssembly))
                {
                    try
                    {
                        references.Add(CreateMetadataReference(referencePath));
                    }
                    catch (Exception ex)
                    {
                        warnings.Add($"Skipped metadata reference '{referencePath}': {ex.Message}");
                    }
                }

                if (references.Count == 0)
                {
                    warnings.Add("No readable metadata references were discovered for semantic diagnostics; fell back to syntax-only.");
                    return null;
                }

                var create = _compilationType
                    .GetMethods(BindingFlags.Public | BindingFlags.Static)
                    .FirstOrDefault(m =>
                    {
                        if (m.Name != "Create") return false;
                        var ps = m.GetParameters();
                        return ps.Length >= 3 && ps[0].ParameterType == typeof(string);
                    });

                if (create == null)
                {
                    warnings.Add("Roslyn CSharpCompilation.Create was not found; semantic diagnostics fell back to syntax-only.");
                    return null;
                }

                var options = CreateCompilationOptions();
                var createArgs = BindArguments(create.GetParameters(), unityAssembly.name ?? "Reify.ScriptInspect", syntaxTrees, references, options);
                var compilation = create.Invoke(null, createArgs);
                if (compilation == null)
                {
                    warnings.Add("Roslyn returned a null compilation object; semantic diagnostics fell back to syntax-only.");
                    return null;
                }

                var diagnostics = InvokeMethod(compilation, "GetDiagnostics") as IEnumerable;
                if (diagnostics == null)
                {
                    warnings.Add("Compilation.GetDiagnostics returned no enumerable diagnostics; semantic diagnostics fell back to syntax-only.");
                    return null;
                }

                var filtered = new List<DiagnosticEnvelope>();
                foreach (var diagnostic in diagnostics)
                {
                    var envelope = ToDiagnosticEnvelope(diagnostic, ReadAllLines(null, absolutePath), stage: "semantic");
                    if (envelope == null) continue;

                    var path = envelope.SourcePath;
                    if (!string.IsNullOrEmpty(path) && NormalizePath(path) != targetNormalized)
                        continue;

                    if (string.IsNullOrEmpty(path))
                        envelope.AssetPath = assetPath;

                    envelope.AssetPath ??= assetPath;
                    filtered.Add(envelope);
                }

                return DeduplicateDiagnostics(filtered);
            }

            private object CreateCompilationOptions()
            {
                object outputKind;
                try { outputKind = Enum.Parse(_outputKindType, "DynamicallyLinkedLibrary"); }
                catch
                {
                    return null;
                }

                var constructors = _compilationOptionsType
                    .GetConstructors(BindingFlags.Public | BindingFlags.Instance)
                    .OrderBy(c => c.GetParameters().Length)
                    .ToArray();

                foreach (var ctor in constructors)
                {
                    try
                    {
                        var args = BindArguments(ctor.GetParameters(), outputKind);
                        var options = ctor.Invoke(args);
                        if (options != null) return options;
                    }
                    catch
                    {
                        // Try the next overload.
                    }
                }

                return null;
            }

            private object CreateMetadataReference(string path)
            {
                var createFromFile = _metadataReferenceType
                    .GetMethods(BindingFlags.Public | BindingFlags.Static)
                    .FirstOrDefault(m =>
                    {
                        if (m.Name != "CreateFromFile") return false;
                        var ps = m.GetParameters();
                        return ps.Length > 0 && ps[0].ParameterType == typeof(string);
                    });

                if (createFromFile == null)
                    throw new InvalidOperationException("MetadataReference.CreateFromFile(string, ...) was not found.");

                var args = BindArguments(createFromFile.GetParameters(), path);
                return createFromFile.Invoke(null, args);
            }

            private List<DiagnosticEnvelope> GetSyntaxDiagnostics(object syntaxTree, string text)
            {
                var diagnostics = InvokeMethod(syntaxTree, "GetDiagnostics") as IEnumerable;
                var lines = ReadAllLines(text, null);
                var result = new List<DiagnosticEnvelope>();
                foreach (var diagnostic in diagnostics ?? Array.Empty<object>())
                {
                    var envelope = ToDiagnosticEnvelope(diagnostic, lines, stage: "syntax");
                    if (envelope != null)
                        result.Add(envelope);
                }
                return DeduplicateDiagnostics(result);
            }

            private static List<DiagnosticEnvelope> DeduplicateDiagnostics(List<DiagnosticEnvelope> diagnostics)
            {
                return diagnostics
                    .GroupBy(d => $"{d.Stage}|{d.Id}|{d.Message}|{d.Line}|{d.Column}|{d.EndLine}|{d.EndColumn}|{d.SourcePath}")
                    .Select(g => g.First())
                    .OrderBy(d => d.Line ?? int.MaxValue)
                    .ThenBy(d => d.Column ?? int.MaxValue)
                    .ThenBy(d => d.Id, StringComparer.Ordinal)
                    .ToList();
            }

            private static DiagnosticEnvelope ToDiagnosticEnvelope(object diagnostic, string[] sourceLines, string stage)
            {
                if (diagnostic == null) return null;

                var location = GetMemberValue(diagnostic, "Location");
                var lineSpan = location != null ? InvokeMethod(location, "GetLineSpan") : null;

                var sourcePath = lineSpan != null ? GetMemberValue(lineSpan, "Path") as string : null;
                var start = lineSpan != null ? GetMemberValue(lineSpan, "StartLinePosition") : null;
                var end = lineSpan != null ? GetMemberValue(lineSpan, "EndLinePosition") : null;

                var line = ReadLinePosition(start, "Line");
                var column = ReadLinePosition(start, "Character");
                var endLine = ReadLinePosition(end, "Line");
                var endColumn = ReadLinePosition(end, "Character");

                var oneBasedLine = line.HasValue ? line.Value + 1 : (int?)null;
                var oneBasedColumn = column.HasValue ? column.Value + 1 : (int?)null;
                var oneBasedEndLine = endLine.HasValue ? endLine.Value + 1 : (int?)null;
                var oneBasedEndColumn = endColumn.HasValue ? endColumn.Value + 1 : (int?)null;

                return new DiagnosticEnvelope
                {
                    Stage = stage,
                    Severity = (GetMemberValue(diagnostic, "Severity")?.ToString() ?? "info").ToLowerInvariant(),
                    Id = GetMemberValue(diagnostic, "Id")?.ToString(),
                    Message = InvokeMethod(diagnostic, "GetMessage") as string ?? diagnostic.ToString(),
                    Line = oneBasedLine,
                    Column = oneBasedColumn,
                    EndLine = oneBasedEndLine,
                    EndColumn = oneBasedEndColumn,
                    SourcePath = sourcePath,
                    SourceSnippet = ExtractSourceSnippet(sourceLines, oneBasedLine)
                };
            }

            private void CollectTypeSummaries(object node, string text, string currentNamespace, List<object> classes)
            {
                if (node == null) return;

                var nodeTypeName = node.GetType().Name;
                var namespaceForChildren = currentNamespace;
                if (nodeTypeName == "NamespaceDeclarationSyntax" || nodeTypeName == "FileScopedNamespaceDeclarationSyntax")
                {
                    var localNamespace = GetMemberValue(node, "Name")?.ToString();
                    namespaceForChildren = string.IsNullOrWhiteSpace(currentNamespace)
                        ? localNamespace
                        : string.IsNullOrWhiteSpace(localNamespace)
                            ? currentNamespace
                            : currentNamespace + "." + localNamespace;
                }

                if (IsSupportedTypeDeclaration(nodeTypeName))
                    classes.Add(ToTypeSummary(node, text, namespaceForChildren));

                foreach (var child in EnumerateNodes(node))
                    CollectTypeSummaries(child, text, namespaceForChildren, classes);
            }

            private static bool IsSupportedTypeDeclaration(string nodeTypeName)
            {
                return nodeTypeName == "ClassDeclarationSyntax" ||
                       nodeTypeName == "StructDeclarationSyntax" ||
                       nodeTypeName == "InterfaceDeclarationSyntax" ||
                       nodeTypeName == "RecordDeclarationSyntax" ||
                       nodeTypeName == "RecordStructDeclarationSyntax";
            }

            private object ToTypeSummary(object node, string text, string declaredNamespace)
            {
                var baseTypes = new List<string>();
                var baseList = GetMemberValue(node, "BaseList");
                if (baseList != null)
                {
                    foreach (var item in GetEnumerableMember(baseList, "Types"))
                    {
                        var typeSyntax = GetMemberValue(item, "Type");
                        var typeText = typeSyntax?.ToString();
                        if (!string.IsNullOrWhiteSpace(typeText))
                            baseTypes.Add(typeText);
                    }
                }

                var methods = new List<object>();
                var fields = new List<object>();
                foreach (var member in GetEnumerableMember(node, "Members"))
                    CollectMemberSummary(member, methods, fields);

                var modifiers = GetMemberValue(node, "Modifiers")?.ToString() ?? string.Empty;

                return new
                {
                    kind       = NormalizeTypeKind(node.GetType().Name),
                    name       = GetMemberValue(node, "Identifier")?.ToString(),
                    @namespace = declaredNamespace,
                    base_type  = baseTypes.FirstOrDefault(),
                    base_types = baseTypes.ToArray(),
                    line       = ReadStartLine(node),
                    visibility = VisibilityOf(modifiers, fallbackVisibility: "internal"),
                    modifiers  = modifiers,
                    attributes = CollectAttributes(node),
                    methods    = methods.ToArray(),
                    fields     = fields.ToArray()
                };
            }

            private void CollectMemberSummary(object member, List<object> methods, List<object> fields)
            {
                if (member == null) return;

                var nodeTypeName = member.GetType().Name;
                if (nodeTypeName == "MethodDeclarationSyntax")
                {
                    var parameters = new List<object>();
                    foreach (var parameter in GetEnumerableMember(GetMemberValue(member, "ParameterList"), "Parameters"))
                    {
                        parameters.Add(new
                        {
                            name        = GetMemberValue(parameter, "Identifier")?.ToString(),
                            type        = GetMemberValue(parameter, "Type")?.ToString(),
                            modifiers   = GetMemberValue(parameter, "Modifiers")?.ToString(),
                            has_default = GetMemberValue(parameter, "Default") != null
                        });
                    }

                    methods.Add(new
                    {
                        kind            = "method",
                        name            = GetMemberValue(member, "Identifier")?.ToString(),
                        return_type     = GetMemberValue(member, "ReturnType")?.ToString(),
                        line            = ReadStartLine(member),
                        visibility      = VisibilityOf(GetMemberValue(member, "Modifiers")?.ToString()),
                        modifiers       = GetMemberValue(member, "Modifiers")?.ToString(),
                        attributes      = CollectAttributes(member),
                        parameter_count = parameters.Count,
                        parameters      = parameters.ToArray()
                    });
                    return;
                }

                if (nodeTypeName == "ConstructorDeclarationSyntax")
                {
                    var parameters = new List<object>();
                    foreach (var parameter in GetEnumerableMember(GetMemberValue(member, "ParameterList"), "Parameters"))
                    {
                        parameters.Add(new
                        {
                            name        = GetMemberValue(parameter, "Identifier")?.ToString(),
                            type        = GetMemberValue(parameter, "Type")?.ToString(),
                            modifiers   = GetMemberValue(parameter, "Modifiers")?.ToString(),
                            has_default = GetMemberValue(parameter, "Default") != null
                        });
                    }

                    methods.Add(new
                    {
                        kind            = "constructor",
                        name            = GetMemberValue(member, "Identifier")?.ToString(),
                        return_type     = (string)null,
                        line            = ReadStartLine(member),
                        visibility      = VisibilityOf(GetMemberValue(member, "Modifiers")?.ToString()),
                        modifiers       = GetMemberValue(member, "Modifiers")?.ToString(),
                        attributes      = CollectAttributes(member),
                        parameter_count = parameters.Count,
                        parameters      = parameters.ToArray()
                    });
                    return;
                }

                if (nodeTypeName == "FieldDeclarationSyntax")
                {
                    var declaration = GetMemberValue(member, "Declaration");
                    var typeName = GetMemberValue(declaration, "Type")?.ToString();
                    foreach (var variable in GetEnumerableMember(declaration, "Variables"))
                    {
                        fields.Add(new
                        {
                            name       = GetMemberValue(variable, "Identifier")?.ToString(),
                            type       = typeName,
                            line       = ReadStartLine(variable),
                            visibility = VisibilityOf(GetMemberValue(member, "Modifiers")?.ToString()),
                            modifiers  = GetMemberValue(member, "Modifiers")?.ToString(),
                            attributes = CollectAttributes(member)
                        });
                    }
                }
            }

            private static string[] CollectAttributes(object node)
            {
                var attributes = new List<string>();
                foreach (var list in GetEnumerableMember(node, "AttributeLists"))
                {
                    foreach (var attr in GetEnumerableMember(list, "Attributes"))
                    {
                        var name = GetMemberValue(attr, "Name")?.ToString();
                        if (!string.IsNullOrWhiteSpace(name))
                            attributes.Add(name);
                    }
                }
                return attributes.ToArray();
            }

            private static int? ReadStartLine(object node)
            {
                var location = node != null ? InvokeMethod(node, "GetLocation") : null;
                var lineSpan = location != null ? InvokeMethod(location, "GetLineSpan") : null;
                var start = lineSpan != null ? GetMemberValue(lineSpan, "StartLinePosition") : null;
                var line = ReadLinePosition(start, "Line");
                return line.HasValue ? line.Value + 1 : (int?)null;
            }

            private static int? ReadLinePosition(object linePosition, string propertyName)
            {
                if (linePosition == null) return null;
                var value = GetMemberValue(linePosition, propertyName);
                if (value == null) return null;
                try { return Convert.ToInt32(value); }
                catch { return null; }
            }

            private static IEnumerable EnumerateNodes(object node)
            {
                return InvokeMethod(node, "ChildNodes") as IEnumerable ?? Array.Empty<object>();
            }

            private static IEnumerable GetEnumerableMember(object instance, string memberName)
            {
                if (instance == null) return Array.Empty<object>();
                return GetMemberValue(instance, memberName) as IEnumerable ?? Array.Empty<object>();
            }

            private static object InvokeMethod(object instance, string methodName)
            {
                if (instance == null) return null;

                var methods = instance.GetType()
                    .GetMethods(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance)
                    .Where(m => m.Name == methodName)
                    .OrderBy(m => m.GetParameters().Length)
                    .ToArray();
                foreach (var method in methods)
                {
                    try
                    {
                        return method.Invoke(instance, BindArguments(method.GetParameters()));
                    }
                    catch
                    {
                        // Try the next overload.
                    }
                }
                return null;
            }

            private static object[] BindArguments(ParameterInfo[] parameters, params object[] preferred)
            {
                var args = new object[parameters.Length];
                for (var i = 0; i < parameters.Length; i++)
                {
                    if (i < preferred.Length && preferred[i] != null && parameters[i].ParameterType.IsInstanceOfType(preferred[i]))
                    {
                        args[i] = preferred[i];
                        continue;
                    }

                    if (i < preferred.Length && preferred[i] == null)
                    {
                        args[i] = null;
                        continue;
                    }

                    var parameter = parameters[i];
                    if (parameter.Name == "path" && preferred.Length > 1 && preferred[1] is string path)
                    {
                        args[i] = path;
                        continue;
                    }

                    if (parameter.ParameterType == typeof(Encoding))
                    {
                        args[i] = Encoding.UTF8;
                        continue;
                    }

                    if (parameter.HasDefaultValue)
                    {
                        args[i] = parameter.DefaultValue;
                        continue;
                    }

                    if (parameter.ParameterType.IsValueType)
                    {
                        args[i] = Activator.CreateInstance(parameter.ParameterType);
                        continue;
                    }

                    args[i] = null;
                }
                return args;
            }

            private static string[] ReadAllLines(string text, string absolutePath)
            {
                if (text != null)
                    return text.Replace("\r\n", "\n").Split('\n');
                if (!string.IsNullOrEmpty(absolutePath) && File.Exists(absolutePath))
                    return File.ReadAllText(absolutePath).Replace("\r\n", "\n").Split('\n');
                return Array.Empty<string>();
            }

            private static string ExtractSourceSnippet(string[] lines, int? oneBasedLine)
            {
                if (!oneBasedLine.HasValue) return null;
                var index = oneBasedLine.Value - 1;
                if (index < 0 || index >= lines.Length) return null;
                return lines[index];
            }

            private static object GetMemberValue(object instance, string memberName)
            {
                if (instance == null) return null;

                var type = instance.GetType();
                var property = type.GetProperty(memberName, BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static);
                if (property != null)
                    return property.GetValue(instance);

                var field = type.GetField(memberName, BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static);
                if (field != null)
                    return field.GetValue(instance);

                return null;
            }

            private static string VisibilityOf(string modifiers, string fallbackVisibility = "private")
            {
                if (string.IsNullOrWhiteSpace(modifiers)) return fallbackVisibility;
                if (modifiers.Contains("public")) return "public";
                if (modifiers.Contains("protected internal")) return "protected_internal";
                if (modifiers.Contains("internal protected")) return "protected_internal";
                if (modifiers.Contains("protected")) return "protected";
                if (modifiers.Contains("internal")) return "internal";
                if (modifiers.Contains("private")) return "private";
                return "private";
            }

            private static string NormalizeTypeKind(string nodeTypeName)
            {
                return nodeTypeName switch
                {
                    "ClassDeclarationSyntax" => "class",
                    "StructDeclarationSyntax" => "struct",
                    "InterfaceDeclarationSyntax" => "interface",
                    "RecordDeclarationSyntax" => "record",
                    "RecordStructDeclarationSyntax" => "record_struct",
                    _ => nodeTypeName
                };
            }

            private static string NormalizePath(string path)
            {
                if (string.IsNullOrWhiteSpace(path)) return null;
                return Path.GetFullPath(path)
                    .Replace(Path.AltDirectorySeparatorChar, Path.DirectorySeparatorChar)
                    .TrimEnd(Path.DirectorySeparatorChar)
                    .ToUpperInvariant();
            }

            private static string ToAbsoluteProjectPath(string path)
            {
                if (string.IsNullOrWhiteSpace(path)) return path;
                if (Path.IsPathRooted(path)) return Path.GetFullPath(path);

                var projectRoot = Path.GetDirectoryName(UnityEngine.Application.dataPath)
                    ?? throw new InvalidOperationException("Could not resolve Unity project root.");
                return Path.GetFullPath(Path.Combine(projectRoot, path));
            }

            private static UnityEditor.Compilation.Assembly FindUnityCompilationAssembly(string absolutePath)
            {
                var target = NormalizePath(absolutePath);
                foreach (var asm in CompilationPipeline.GetAssemblies())
                {
                    foreach (var sourceFile in asm.sourceFiles ?? Array.Empty<string>())
                    {
                        if (NormalizePath(ToAbsoluteProjectPath(sourceFile)) == target)
                            return asm;
                    }
                }
                return null;
            }

            private static IEnumerable<string> CollectReferencePaths(UnityEditor.Compilation.Assembly unityAssembly)
            {
                var paths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

                foreach (var path in unityAssembly.compiledAssemblyReferences ?? Array.Empty<string>())
                    AddReferencePath(paths, path);

                foreach (var asm in AppDomain.CurrentDomain.GetAssemblies())
                {
                    try
                    {
                        if (asm.IsDynamic) continue;
                        AddReferencePath(paths, asm.Location);
                    }
                    catch
                    {
                        // Ignore reflection-only / dynamic assembly location failures.
                    }
                }

                return paths;
            }

            private static void AddReferencePath(HashSet<string> paths, string path)
            {
                if (string.IsNullOrWhiteSpace(path)) return;
                if (!File.Exists(path)) return;
                paths.Add(Path.GetFullPath(path));
            }

            private static Type FindType(params string[] candidates)
            {
                foreach (var candidate in candidates)
                {
                    var type = Type.GetType(candidate, throwOnError: false);
                    if (type != null) return type;
                }

                foreach (var asm in AppDomain.CurrentDomain.GetAssemblies())
                {
                    foreach (var candidate in candidates)
                    {
                        var type = asm.GetType(candidate, false);
                        if (type != null) return type;
                    }
                }

                return null;
            }
        }

        private sealed class DiagnosticEnvelope
        {
            public string Stage { get; set; }
            public string Severity { get; set; }
            public string Id { get; set; }
            public string Message { get; set; }
            public int? Line { get; set; }
            public int? Column { get; set; }
            public int? EndLine { get; set; }
            public int? EndColumn { get; set; }
            public string SourcePath { get; set; }
            public string SourceSnippet { get; set; }
            public string AssetPath { get; set; }

            public object ToEnvelope() => new
            {
                stage          = Stage,
                severity       = Severity,
                id             = Id,
                message        = Message,
                asset_path     = AssetPath,
                line           = Line,
                column         = Column,
                end_line       = EndLine,
                end_column     = EndColumn,
                source_snippet = SourceSnippet
            };
        }
    }
}
