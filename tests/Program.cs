using System;

// 独立测试入口：运行 RenderPackageChecks 的全部检查（与上游 Agamemnon 仓库同一检查集）。
internal static class Program
{
    private static int _passed;
    private static void Check(bool condition, string name)
    {
        if (!condition) throw new Exception("FAIL: " + name);
        _passed++; Console.WriteLine("PASS: " + name);
    }
    private static int Main(string[] args)
    {
        string output = args.Length > 0 ? args[0] : "artifacts";
        try { System.IO.Directory.CreateDirectory(output); RenderPackageChecks.Run(Check, output); }
        catch (Exception ex)
        {
            for (Exception current = ex; current != null; current = current.InnerException)
                Console.WriteLine(current.GetType().Name + ": " + current.Message + Environment.NewLine + current.StackTrace);
            return 1;
        }
        Console.WriteLine("Passed " + _passed + " checks; frames written to " + System.IO.Path.GetFullPath(output) + "; no hardware accessed.");
        return _passed == 0 ? 1 : 0;
    }
}
