using System;
using System.IO;
namespace HearthstoneRank
{
    static class IconBuilder
    {
        static void Main(string[] args)
        { using (var icon = Art.MakeIcon()) using (var stream = File.Create(args[0])) icon.Save(stream); }
    }
}
