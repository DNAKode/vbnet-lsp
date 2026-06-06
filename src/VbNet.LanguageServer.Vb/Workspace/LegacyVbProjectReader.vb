Imports System.Collections.Immutable
Imports System.IO
Imports System.Xml.Linq
Imports Microsoft.CodeAnalysis
Imports Microsoft.CodeAnalysis.VisualBasic

Namespace Workspace

    Friend NotInheritable Class LegacyVbProjectInfo
        Public Property ProjectPath As String
        Public Property AssemblyName As String
        Public Property RootNamespace As String
        Public Property OutputKind As OutputKind
        Public Property MyType As String
        Public Property OptionStrict As OptionStrict
        Public Property OptionInfer As Boolean
        Public Property OptionExplicit As Boolean
        Public Property OptionCompareText As Boolean
        Public Property GlobalImports As ImmutableArray(Of GlobalImport)
        Public Property Documents As ImmutableArray(Of String)
        Public Property References As ImmutableArray(Of MetadataReference)
        Public Property ProjectReferences As ImmutableArray(Of String)
        Public Property Warnings As ImmutableArray(Of String)
    End Class

    Friend NotInheritable Class LegacyVbProjectReader
        Private Sub New()
        End Sub

        Public Shared Function TryRead(projectPath As String) As LegacyVbProjectInfo
            If String.IsNullOrWhiteSpace(projectPath) OrElse Not File.Exists(projectPath) Then
                Return Nothing
            End If

            Dim document As XDocument
            Try
                document = XDocument.Load(projectPath)
            Catch
                Return Nothing
            End Try

            Dim root = document.Root
            If root Is Nothing OrElse root.Attribute("Sdk") IsNot Nothing Then
                Return Nothing
            End If

            Dim projectDir = Path.GetDirectoryName(Path.GetFullPath(projectPath))
            Dim targetFrameworkVersion = GetProperty(root, "TargetFrameworkVersion")
            If String.IsNullOrWhiteSpace(targetFrameworkVersion) OrElse Not targetFrameworkVersion.StartsWith("v4.", StringComparison.OrdinalIgnoreCase) Then
                Return Nothing
            End If

            Dim warnings = ImmutableArray.CreateBuilder(Of String)()
            AddProjectModelWarnings(root, warnings)
            Dim references = ResolveReferences(root, projectDir, targetFrameworkVersion, warnings)
            If references.Length = 0 Then
                Return Nothing
            End If

            Dim assemblyName = GetProperty(root, "AssemblyName")
            If String.IsNullOrWhiteSpace(assemblyName) Then
                assemblyName = Path.GetFileNameWithoutExtension(projectPath)
            End If

            Return New LegacyVbProjectInfo With {
                .ProjectPath = Path.GetFullPath(projectPath),
                .AssemblyName = assemblyName,
                .RootNamespace = GetProperty(root, "RootNamespace"),
                .OutputKind = GetOutputKind(GetProperty(root, "OutputType")),
                .MyType = GetProperty(root, "MyType"),
                .OptionStrict = GetOptionStrict(GetProperty(root, "OptionStrict")),
                .OptionInfer = GetBooleanProperty(GetProperty(root, "OptionInfer"), defaultValue:=True),
                .OptionExplicit = GetBooleanProperty(GetProperty(root, "OptionExplicit"), defaultValue:=True),
                .OptionCompareText = String.Equals(GetProperty(root, "OptionCompare"), "Text", StringComparison.OrdinalIgnoreCase),
                .GlobalImports = ResolveImports(root),
                .Documents = ResolveCompileItems(root, projectDir),
                .References = references,
                .ProjectReferences = ResolveProjectReferences(root, projectDir),
                .Warnings = warnings.ToImmutable()
            }
        End Function

        Public Shared Function GetProjectPathsFromSlnx(solutionPath As String) As ImmutableArray(Of String)
            If String.IsNullOrWhiteSpace(solutionPath) OrElse Not File.Exists(solutionPath) Then
                Return ImmutableArray(Of String).Empty
            End If

            Dim document As XDocument
            Try
                document = XDocument.Load(solutionPath)
            Catch
                Return ImmutableArray(Of String).Empty
            End Try

            Dim solutionDir = Path.GetDirectoryName(Path.GetFullPath(solutionPath))
            Dim builder = ImmutableArray.CreateBuilder(Of String)()

            For Each element In document.Descendants().Where(Function(e) String.Equals(e.Name.LocalName, "Project", StringComparison.OrdinalIgnoreCase))
                Dim relativePath = element.Attribute("Path")?.Value
                If String.IsNullOrWhiteSpace(relativePath) OrElse Not relativePath.EndsWith(".vbproj", StringComparison.OrdinalIgnoreCase) Then
                    Continue For
                End If

                Dim projectPath = If(System.IO.Path.IsPathRooted(relativePath), relativePath, System.IO.Path.Combine(solutionDir, relativePath))
                If File.Exists(projectPath) Then
                    builder.Add(System.IO.Path.GetFullPath(projectPath))
                End If
            Next

            Return builder.Distinct(StringComparer.OrdinalIgnoreCase).ToImmutableArray()
        End Function

        Private Shared Function GetProperty(root As XElement, name As String) As String
            Dim element = root.Descendants().FirstOrDefault(Function(e) String.Equals(e.Name.LocalName, name, StringComparison.OrdinalIgnoreCase))
            Return If(element?.Value, String.Empty).Trim()
        End Function

        Private Shared Function ResolveCompileItems(root As XElement, projectDir As String) As ImmutableArray(Of String)
            Dim builder = ImmutableArray.CreateBuilder(Of String)()
            For Each element In root.Descendants().Where(Function(e) String.Equals(e.Name.LocalName, "Compile", StringComparison.OrdinalIgnoreCase))
                Dim include = element.Attribute("Include")?.Value
                If String.IsNullOrWhiteSpace(include) Then
                    Continue For
                End If

                Dim documentPath As String = If(System.IO.Path.IsPathRooted(include), include, System.IO.Path.Combine(projectDir, include))
                If File.Exists(documentPath) Then
                    builder.Add(System.IO.Path.GetFullPath(documentPath))
                End If
            Next

            Return builder.Distinct(StringComparer.OrdinalIgnoreCase).ToImmutableArray()
        End Function

        Private Shared Function ResolveImports(root As XElement) As ImmutableArray(Of GlobalImport)
            Dim names = root.Descendants().
                Where(Function(e) String.Equals(e.Name.LocalName, "Import", StringComparison.OrdinalIgnoreCase)).
                Select(Function(e) e.Attribute("Include")?.Value).
                Where(Function(value) Not String.IsNullOrWhiteSpace(value)).
                Select(Function(value) value.Trim()).
                Distinct(StringComparer.OrdinalIgnoreCase)

            Dim builder = ImmutableArray.CreateBuilder(Of GlobalImport)()
            For Each name In names
                Try
                    builder.Add(GlobalImport.Parse(name))
                Catch
                End Try
            Next

            Return builder.ToImmutable()
        End Function

        Private Shared Function ResolveReferences(root As XElement, projectDir As String, targetFrameworkVersion As String, warnings As ImmutableArray(Of String).Builder) As ImmutableArray(Of MetadataReference)
            Dim referenceDir = GetReferenceAssemblyDirectory(targetFrameworkVersion)
            If String.IsNullOrWhiteSpace(referenceDir) OrElse Not Directory.Exists(referenceDir) Then
                Return ImmutableArray(Of MetadataReference).Empty
            End If

            Dim builder = ImmutableArray.CreateBuilder(Of MetadataReference)()
            Dim addedPaths As New HashSet(Of String)(StringComparer.OrdinalIgnoreCase)
            Dim referenceNames = root.Descendants().
                Where(Function(e) String.Equals(e.Name.LocalName, "Reference", StringComparison.OrdinalIgnoreCase)).
                Select(Function(e) e.Attribute("Include")?.Value).
                Where(Function(value) Not String.IsNullOrWhiteSpace(value)).
                Select(Function(value) value.Split(","c)(0).Trim()).
                ToList()

            For Each referenceElement In root.Descendants().Where(Function(e) String.Equals(e.Name.LocalName, "Reference", StringComparison.OrdinalIgnoreCase))
                Dim hintPath = referenceElement.Elements().FirstOrDefault(Function(e) String.Equals(e.Name.LocalName, "HintPath", StringComparison.OrdinalIgnoreCase))?.Value
                If String.IsNullOrWhiteSpace(hintPath) Then
                    Continue For
                End If

                Dim resolvedHintPath = If(System.IO.Path.IsPathRooted(hintPath), hintPath, System.IO.Path.Combine(projectDir, hintPath))
                If Not AddReference(builder, addedPaths, resolvedHintPath) Then
                    warnings.Add($"Assembly reference hint path was not resolved: {hintPath}.")
                End If
            Next

            EnsureReference(referenceNames, "mscorlib")
            EnsureReference(referenceNames, "Microsoft.VisualBasic")
            EnsureReference(referenceNames, "System")
            EnsureReference(referenceNames, "System.Core")

            Dim unresolvedReferences As New List(Of String)()
            For Each referenceName In referenceNames.Distinct(StringComparer.OrdinalIgnoreCase)
                Dim referencePath As String = System.IO.Path.Combine(referenceDir, referenceName & ".dll")
                If Not AddReference(builder, addedPaths, referencePath) AndAlso
                    Not root.Descendants().
                        Where(Function(e) String.Equals(e.Name.LocalName, "Reference", StringComparison.OrdinalIgnoreCase)).
                        Any(Function(e) String.Equals(e.Attribute("Include")?.Value?.Split(","c)(0).Trim(), referenceName, StringComparison.OrdinalIgnoreCase) AndAlso
                            e.Elements().Any(Function(child) String.Equals(child.Name.LocalName, "HintPath", StringComparison.OrdinalIgnoreCase))) Then
                    unresolvedReferences.Add(referenceName)
                End If
            Next

            If unresolvedReferences.Count > 0 Then
                warnings.Add($"Some assembly references were not resolved by the legacy fallback: {String.Join(", ", unresolvedReferences.Distinct(StringComparer.OrdinalIgnoreCase))}.")
            End If

            AddPackageReferences(builder, addedPaths, root, projectDir, targetFrameworkVersion, warnings)
            AddComReferences(builder, addedPaths, root, projectDir, warnings)

            Return builder.ToImmutable()
        End Function

        Private Shared Sub AddProjectModelWarnings(root As XElement, warnings As ImmutableArray(Of String).Builder)
            If root.Descendants().Any(Function(e) e.Attribute("Condition") IsNot Nothing) Then
                warnings.Add("MSBuild Condition attributes were found. The legacy fallback does not fully evaluate conditional project logic, so configuration-specific files or references may be incomplete.")
            End If

            If root.Elements().Any(Function(e) String.Equals(e.Name.LocalName, "Import", StringComparison.OrdinalIgnoreCase) AndAlso e.Attribute("Project") IsNot Nothing) Then
                warnings.Add("Imported MSBuild props/targets were found. The legacy fallback does not execute imported build logic, so generated files or custom references may be missing.")
            End If
        End Sub

        Private Shared Function ResolveProjectReferences(root As XElement, projectDir As String) As ImmutableArray(Of String)
            Dim builder = ImmutableArray.CreateBuilder(Of String)()

            For Each element In root.Descendants().Where(Function(e) String.Equals(e.Name.LocalName, "ProjectReference", StringComparison.OrdinalIgnoreCase))
                Dim include = element.Attribute("Include")?.Value
                If String.IsNullOrWhiteSpace(include) Then
                    Continue For
                End If

                Dim referencePath = If(System.IO.Path.IsPathRooted(include), include, System.IO.Path.Combine(projectDir, include))
                If File.Exists(referencePath) Then
                    builder.Add(System.IO.Path.GetFullPath(referencePath))
                End If
            Next

            Return builder.Distinct(StringComparer.OrdinalIgnoreCase).ToImmutableArray()
        End Function

        Private Shared Sub AddPackageReferences(builder As ImmutableArray(Of MetadataReference).Builder, addedPaths As HashSet(Of String), root As XElement, projectDir As String, targetFrameworkVersion As String, warnings As ImmutableArray(Of String).Builder)
            Dim packageAssemblies = ResolvePackagesConfigAssemblies(projectDir, targetFrameworkVersion).
                Concat(ResolvePackageReferenceAssemblies(root, targetFrameworkVersion)).
                Distinct(StringComparer.OrdinalIgnoreCase).
                ToList()

            For Each assemblyPath In packageAssemblies
                AddReference(builder, addedPaths, assemblyPath)
            Next

            If HasPackagesConfig(projectDir) AndAlso packageAssemblies.Count = 0 Then
                warnings.Add("packages.config was found, but no compatible package assemblies were resolved. Run NuGet restore and check package target frameworks if package symbols are missing.")
            End If

            If HasPackageReference(root) AndAlso packageAssemblies.Count = 0 Then
                warnings.Add("PackageReference items were found, but no compatible package assemblies were resolved from the global NuGet cache. Run restore if package symbols are missing.")
            End If
        End Sub

        Private Shared Function ResolvePackagesConfigAssemblies(projectDir As String, targetFrameworkVersion As String) As IEnumerable(Of String)
            Dim packagesConfig = System.IO.Path.Combine(projectDir, "packages.config")
            If Not File.Exists(packagesConfig) Then
                Return Enumerable.Empty(Of String)()
            End If

            Dim document As XDocument
            Try
                document = XDocument.Load(packagesConfig)
            Catch
                Return Enumerable.Empty(Of String)()
            End Try

            Dim packagesRoot = System.IO.Path.GetFullPath(System.IO.Path.Combine(projectDir, "..", "packages"))
            Return document.Descendants().
                Where(Function(e) String.Equals(e.Name.LocalName, "package", StringComparison.OrdinalIgnoreCase)).
                SelectMany(Function(e)
                               Dim id = e.Attribute("id")?.Value
                               Dim version = e.Attribute("version")?.Value
                               If String.IsNullOrWhiteSpace(id) OrElse String.IsNullOrWhiteSpace(version) Then
                                   Return Enumerable.Empty(Of String)()
                               End If

                               Return ResolvePackageLibAssemblies(System.IO.Path.Combine(packagesRoot, id & "." & version), targetFrameworkVersion)
                           End Function)
        End Function

        Private Shared Function ResolvePackageReferenceAssemblies(root As XElement, targetFrameworkVersion As String) As IEnumerable(Of String)
            Dim globalPackages = Environment.GetEnvironmentVariable("NUGET_PACKAGES")
            If String.IsNullOrWhiteSpace(globalPackages) Then
                globalPackages = System.IO.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".nuget", "packages")
            End If

            Return root.Descendants().
                Where(Function(e) String.Equals(e.Name.LocalName, "PackageReference", StringComparison.OrdinalIgnoreCase)).
                SelectMany(Function(e)
                               Dim id = e.Attribute("Include")?.Value
                               Dim version = e.Attribute("Version")?.Value
                               If String.IsNullOrWhiteSpace(version) Then
                                   version = e.Elements().FirstOrDefault(Function(child) String.Equals(child.Name.LocalName, "Version", StringComparison.OrdinalIgnoreCase))?.Value
                               End If

                               If String.IsNullOrWhiteSpace(id) OrElse String.IsNullOrWhiteSpace(version) Then
                                   Return Enumerable.Empty(Of String)()
                               End If

                               Return ResolvePackageLibAssemblies(System.IO.Path.Combine(globalPackages, id.ToLowerInvariant(), version.ToLowerInvariant()), targetFrameworkVersion)
                           End Function)
        End Function

        Private Shared Function ResolvePackageLibAssemblies(packageDir As String, targetFrameworkVersion As String) As IEnumerable(Of String)
            Dim libDir = System.IO.Path.Combine(packageDir, "lib")
            If Not Directory.Exists(libDir) Then
                Return Enumerable.Empty(Of String)()
            End If

            Dim frameworkFolder = ChooseBestPackageFrameworkFolder(libDir, targetFrameworkVersion)
            If String.IsNullOrWhiteSpace(frameworkFolder) Then
                Return Enumerable.Empty(Of String)()
            End If

            Return Directory.EnumerateFiles(frameworkFolder, "*.dll", SearchOption.TopDirectoryOnly)
        End Function

        Private Shared Function ChooseBestPackageFrameworkFolder(libDir As String, targetFrameworkVersion As String) As String
            Dim preferred = GetPackageFrameworkCandidates(targetFrameworkVersion)
            Dim folders = Directory.EnumerateDirectories(libDir).ToList()

            For Each candidate In preferred
                Dim folder = folders.FirstOrDefault(Function(path) String.Equals(System.IO.Path.GetFileName(path), candidate, StringComparison.OrdinalIgnoreCase))
                If folder IsNot Nothing Then
                    Return folder
                End If
            Next

            Return folders.FirstOrDefault(Function(path) System.IO.Path.GetFileName(path).StartsWith("net4", StringComparison.OrdinalIgnoreCase))
        End Function

        Private Shared Function GetPackageFrameworkCandidates(targetFrameworkVersion As String) As String()
            Dim normalized = targetFrameworkVersion.TrimStart("v"c, "V"c).Replace(".", String.Empty)
            If normalized.StartsWith("4", StringComparison.OrdinalIgnoreCase) Then
                normalized = "net" & normalized
            End If

            Return {normalized, "net481", "net48", "net472", "net471", "net47", "net462", "net461", "net46", "net452", "net451", "net45", "net40", "net35", "net20"}
        End Function

        Private Shared Sub AddComReferences(builder As ImmutableArray(Of MetadataReference).Builder, addedPaths As HashSet(Of String), root As XElement, projectDir As String, warnings As ImmutableArray(Of String).Builder)
            Dim comReferences = root.Descendants().Where(Function(e) String.Equals(e.Name.LocalName, "COMReference", StringComparison.OrdinalIgnoreCase) OrElse String.Equals(e.Name.LocalName, "COMFileReference", StringComparison.OrdinalIgnoreCase)).ToList()
            If comReferences.Count = 0 Then
                Return
            End If

            Dim addedAny = False
            For Each element In comReferences
                Dim include = element.Attribute("Include")?.Value
                If String.IsNullOrWhiteSpace(include) Then
                    Continue For
                End If

                For Each buildConfiguration In {"Debug", "Release"}
                    Dim interopPath = System.IO.Path.Combine(projectDir, "obj", buildConfiguration, "Interop." & include & ".dll")
                    addedAny = AddReference(builder, addedPaths, interopPath) OrElse addedAny
                Next
            Next

            If addedAny Then
                warnings.Add("COM references were found. The legacy fallback used existing generated interop assemblies where available; run a full MSBuild build if COM symbols are incomplete.")
            Else
                warnings.Add("COM references were found, but generated interop assemblies were not available. COM symbols may be missing until the project is loaded by full MSBuild or built once.")
            End If
        End Sub

        Private Shared Function HasPackagesConfig(projectDir As String) As Boolean
            Return File.Exists(System.IO.Path.Combine(projectDir, "packages.config"))
        End Function

        Private Shared Function HasPackageReference(root As XElement) As Boolean
            Return root.Descendants().Any(Function(e) String.Equals(e.Name.LocalName, "PackageReference", StringComparison.OrdinalIgnoreCase))
        End Function

        Private Shared Function AddReference(builder As ImmutableArray(Of MetadataReference).Builder, addedPaths As HashSet(Of String), referencePath As String) As Boolean
            If String.IsNullOrWhiteSpace(referencePath) OrElse Not File.Exists(referencePath) Then
                Return False
            End If

            Dim fullPath = System.IO.Path.GetFullPath(referencePath)
            If Not addedPaths.Add(fullPath) Then
                Return False
            End If

            Try
                builder.Add(MetadataReference.CreateFromFile(fullPath))
                Return True
            Catch
                Return False
            End Try
        End Function

        Private Shared Sub EnsureReference(referenceNames As List(Of String), referenceName As String)
            If Not referenceNames.Any(Function(name) String.Equals(name, referenceName, StringComparison.OrdinalIgnoreCase)) Then
                referenceNames.Add(referenceName)
            End If
        End Sub

        Private Shared Function GetReferenceAssemblyDirectory(targetFrameworkVersion As String) As String
            Dim programFilesX86 = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86)
            If String.IsNullOrWhiteSpace(programFilesX86) Then
                Return Nothing
            End If

            Return Path.Combine(
                programFilesX86,
                "Reference Assemblies",
                "Microsoft",
                "Framework",
                ".NETFramework",
                targetFrameworkVersion)
        End Function

        Private Shared Function GetOutputKind(outputType As String) As OutputKind
            If String.Equals(outputType, "Exe", StringComparison.OrdinalIgnoreCase) Then
                Return OutputKind.ConsoleApplication
            End If

            If String.Equals(outputType, "WinExe", StringComparison.OrdinalIgnoreCase) Then
                Return OutputKind.WindowsApplication
            End If

            Return OutputKind.DynamicallyLinkedLibrary
        End Function

        Private Shared Function GetOptionStrict(value As String) As OptionStrict
            If String.Equals(value, "On", StringComparison.OrdinalIgnoreCase) Then
                Return OptionStrict.On
            End If

            Return OptionStrict.Off
        End Function

        Private Shared Function GetBooleanProperty(value As String, defaultValue As Boolean) As Boolean
            If String.IsNullOrWhiteSpace(value) Then
                Return defaultValue
            End If

            Return String.Equals(value, "On", StringComparison.OrdinalIgnoreCase) OrElse
                String.Equals(value, "true", StringComparison.OrdinalIgnoreCase)
        End Function
    End Class

End Namespace
