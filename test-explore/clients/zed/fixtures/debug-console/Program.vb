Module Program
    Sub Main(args As String())
        Console.WriteLine(String.Join(",", args))

        If Environment.GetEnvironmentVariable("VBNET_ZED_DEBUG_FIXTURE") = "1" Then
            Dim logPath = Environment.GetEnvironmentVariable("VBNET_ZED_DEBUG_LOG")
            If String.IsNullOrWhiteSpace(logPath) Then
                logPath = IO.Path.Combine(Environment.CurrentDirectory, "zed-debug-fixture.log")
            End If

            IO.File.AppendAllText(logPath, String.Join(",", args) & Environment.NewLine)
        End If
    End Sub
End Module
