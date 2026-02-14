using System;
using System.IO;
using System.Text;
using Veldrid;
using Veldrid.SPIRV;

namespace IronRose.Rendering
{
    public static class ShaderCompiler
    {
        public static Shader[] CompileGLSL(GraphicsDevice device, string vertexPath, string fragmentPath)
        {
            string vertexCode = File.ReadAllText(vertexPath);
            string fragmentCode = File.ReadAllText(fragmentPath);

            var vertexBytes = Encoding.UTF8.GetBytes(vertexCode);
            var fragmentBytes = Encoding.UTF8.GetBytes(fragmentCode);

            var vertexDesc = new ShaderDescription(ShaderStages.Vertex, vertexBytes, "main");
            var fragmentDesc = new ShaderDescription(ShaderStages.Fragment, fragmentBytes, "main");

            try
            {
                var shaders = device.ResourceFactory.CreateFromSpirv(vertexDesc, fragmentDesc);
                Console.WriteLine($"[ShaderCompiler] Compiled shaders: {vertexPath}, {fragmentPath}");
                return shaders;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[ShaderCompiler] ERROR: {ex.Message}");
                throw;
            }
        }
    }
}
