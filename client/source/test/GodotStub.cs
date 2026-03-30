// Minimal Godot stubs so data loaders compile without the Godot SDK
namespace Godot
{
    public static class GD
    {
        public static void Print(params object[] args)
        {
            foreach (var a in args) System.Console.Write(a);
            System.Console.WriteLine();
        }
        public static void PrintErr(params object[] args)
        {
            System.Console.Error.Write("[ERR] ");
            foreach (var a in args) System.Console.Error.Write(a);
            System.Console.Error.WriteLine();
        }
    }
}
