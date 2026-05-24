using BenchmarkDotNet.Running;

namespace WorldSphereMod.Tests.Bench;

internal class Program
{
    static void Main()
    {
        BenchmarkRunner.Run<AllBenches>();
    }
}
