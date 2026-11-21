using Stabby.World;

namespace Stabby.Viewer
{
    public static class MaterialFile
    {
        public static string DefaultPath => ContentFiles.MaterialsPath;

        public static MaterialLibrary Load(string path) => ContentFiles.Materials(path);
    }
}
