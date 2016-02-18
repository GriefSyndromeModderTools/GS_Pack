using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace GS_Pack
{
    public static class Program
    {
        private static void PrintUsage()
        {
            Console.WriteLine("GS_Pack");
            Console.WriteLine("-------");
            Console.WriteLine("Griefsyndrome data file extractor/packer. By acaly.");
            Console.WriteLine("Usage:");
            Console.WriteLine("  -e <dat> <dir> Extract.");
            Console.WriteLine("  -p <dir> <dat> Pack.");
        }
        private static void Main(string[] args)
        {
            if (args.Length != 3 || args[0] != "-e" && args[0] != "-p")
            {
                PrintUsage();
                return;
            }
            if (args[0] == "-e")
            {
                if (!File.Exists(args[1]))
                {
                    Console.WriteLine("File not exists.");
                    PrintUsage();
                    return;
                }
                if (!Directory.Exists(args[2]))
                {
                    if (!Directory.Exists(Path.GetDirectoryName(args[2])))
                    {
                        Console.WriteLine("Only directory in the lowest level can be created.");
                        PrintUsage();
                        return;
                    }
                }
                else
                {
                    Console.WriteLine("Output directory exists. Overwriting.");
                }
                Console.WriteLine("Extracting...");
                using (var pack = Package.ReadPackageFile(args[1]))
                {
                    Extract(pack, args[2]);
                }
            }
            else if (args[0] == "-p")
            {
                if (!Directory.Exists(args[1]))
                {
                    Console.WriteLine("Input directory not exists.");
                    PrintUsage();
                    return;
                }
                if (File.Exists(args[2]))
                {
                    Console.WriteLine("File exists. Overwriting.");
                }
                Console.WriteLine("Packing...");
                using (var pack = Package.CreateEmptyPackage())
                {
                    Pack(pack, args[1]);
                    pack.SavePackageToFile(args[2]);
                }
            }
        }

        private static void Extract(Package package, string dir)
        {
            if (!Directory.Exists(dir))
            {
                Directory.CreateDirectory(dir);
            }

            var maxLength = package.FileList.Max(file => file.Value.Length);
            byte[] buffer = new byte[maxLength];
            foreach (var file in package.FileList)
            {
                file.Value.Read(buffer, 0);
                var subdir = Path.GetDirectoryName(Path.Combine(dir, file.Key));
                if (!Directory.Exists(subdir))
                {
                    Directory.CreateDirectory(subdir);
                }
                using (var output = File.OpenWrite(Path.Combine(dir, file.Key)))
                {
                    output.Write(buffer, 0, file.Value.Length);
                }
            }
        }

        private static void Pack(Package package, string dir)
        {
            string dir2 = Path.Combine(dir, "./");
            Uri uriDir = new Uri(dir2);
            foreach (var file in Directory.EnumerateFiles(dir, "*.*", SearchOption.AllDirectories))
            {
                Uri uriFile = new Uri(file);
                Uri uriRelative = uriDir.MakeRelativeUri(uriFile);
                string relative = Uri.UnescapeDataString(uriRelative.ToString());
                package.FileList.Add(relative, Package.CreateFileReference(file));
            }
        }
    }
}
