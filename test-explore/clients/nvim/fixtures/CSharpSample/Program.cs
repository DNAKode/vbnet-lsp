using System;

namespace CSharpSample;

public static class Program
{
    public static int Add(int left, int right)
    {
        return left + right;
    }

    public static void Main(string[] args)
    {
        var result = Add(1, 2);
        Console.WriteLine(result);
    }
}
