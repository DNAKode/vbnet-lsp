' Helpers for detecting SDK-style .NET Framework targets in project files.

Imports System.IO
Imports System.Xml.Linq

Namespace Workspace

    Public NotInheritable Class NetFxProjectInspector
        Private Sub New()
        End Sub

        Public Shared Function GetSdkStyleNetFxTargets(projectPath As String) As String()
            If String.IsNullOrWhiteSpace(projectPath) OrElse Not File.Exists(projectPath) Then
                Return Array.Empty(Of String)()
            End If

            Dim document As XDocument
            Try
                document = XDocument.Load(projectPath)
            Catch
                Return Array.Empty(Of String)()
            End Try

            Dim root = document.Root
            If root Is Nothing OrElse Not IsSdkStyleProject(root) Then
                Return Array.Empty(Of String)()
            End If

            Dim targets As New List(Of String)()
            For Each element In root.Descendants()
                Dim name = element.Name.LocalName
                If name = "TargetFramework" OrElse name = "TargetFrameworks" Then
                    Dim value = element.Value
                    If String.IsNullOrWhiteSpace(value) Then
                        Continue For
                    End If

                    For Each target In value.Split(";"c, StringSplitOptions.RemoveEmptyEntries)
                        Dim trimmed = target.Trim()
                        If IsNetFxTarget(trimmed) Then
                            targets.Add(trimmed)
                        End If
                    Next
                End If
            Next

            Return targets.Distinct(StringComparer.OrdinalIgnoreCase).ToArray()
        End Function

        Public Shared Function GetNetFxReferenceFolderName(targetFramework As String) As String
            Dim version = GetNetFxVersion(targetFramework)
            If String.IsNullOrWhiteSpace(version) Then
                Return Nothing
            End If

            Return $"v{version}"
        End Function

        Private Shared Function IsSdkStyleProject(root As XElement) As Boolean
            If root.Attribute("Sdk") IsNot Nothing Then
                Return True
            End If

            Return root.Elements().Any(Function(element) element.Name.LocalName = "Sdk")
        End Function

        Private Shared Function IsNetFxTarget(targetFramework As String) As Boolean
            If String.IsNullOrWhiteSpace(targetFramework) Then
                Return False
            End If

            Return targetFramework.StartsWith("net4", StringComparison.OrdinalIgnoreCase)
        End Function

        Private Shared Function GetNetFxVersion(targetFramework As String) As String
            If Not IsNetFxTarget(targetFramework) Then
                Return Nothing
            End If

            Dim suffix = targetFramework.Substring(4).Trim()
            If suffix.StartsWith(".", StringComparison.OrdinalIgnoreCase) Then
                suffix = suffix.TrimStart("."c)
            End If

            If suffix.Length = 0 Then
                Return "4.0"
            End If

            If suffix.Length = 1 Then
                Return $"4.{suffix}"
            End If

            Return $"4.{suffix(0)}.{suffix(1)}"
        End Function
    End Class

End Namespace
