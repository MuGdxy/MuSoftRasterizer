using System;
using System.Collections.Generic;
using System.Drawing;
using System.Threading;
using System.Windows.Forms;
using GlmNet;
using System.Runtime.InteropServices;
using ObjLoader.Loader.Loaders;
using System.IO;

namespace SoftRasterizer
{
    static class Program
    {
        /// <summary>
        /// The main entry point for the application.
        /// </summary>
        [STAThread]
        static void Main()
        {
            var app = new App();
            app.Run();
        }
    }

    class Time
    {
        public static float DeltaTime => deltaTime;
        public static float minInterval = 0.02f;
        private static float deltaTime = 0.02f;
        private static DateTime lastTime;
        public static void Setup()
        {
            lastTime = DateTime.Now.ToUniversalTime();
        }
        public static void Update()
        {
            deltaTime = (float)(DateTime.Now.ToUniversalTime() - lastTime).TotalSeconds;
            lastTime = DateTime.Now.ToUniversalTime();
        }
        public static void UpdateWithFPSControll()
        {
            deltaTime = (float)(DateTime.Now.ToUniversalTime() - lastTime).TotalSeconds;
            if (deltaTime < minInterval)
            {
                Thread.Sleep((int)(1000 * (minInterval - deltaTime)));
                deltaTime = (float)(DateTime.Now.ToUniversalTime() - lastTime).TotalSeconds;
            }
            lastTime = DateTime.Now.ToUniversalTime();
        }
    }

    class App
    {
        private Form form;

        private GraphicsBuffer graphicsBuffer;
        private Graphics presentGraphics;
        private int width = 400;
        private int height = 300;

        private Font font = new Font(new FontFamily("Consolas"), 14);

        mat4 MVP;
        mat4 M;
        mat4 V;
        mat4 P;
        Model model;
        vec3 lightPos;
        private vec2 Clip2Screen(vec4 v)
        {
            vec2 v2 = new vec2();
            v2.x = width/2 + v.x * width/2;
            v2.y = height/2 - v.y * height/2;
            return v2;
        }
    
        public void Run()
        {
            form = new Form
            {
                Size = new Size(width, height),
                StartPosition = FormStartPosition.CenterScreen
            };
            form.Show();
            form.Resize += (o, args) =>
            {
                width = form.Width;
                height = form.Height;
                graphicsBuffer?.Dispose();
                graphicsBuffer = new GraphicsBuffer(form);
                presentGraphics?.Dispose();
                presentGraphics = form.CreateGraphics();
            };

            graphicsBuffer = new GraphicsBuffer(form);
            presentGraphics = form.CreateGraphics();

            Time.Setup();

            model = new Model("Suzanne.obj");
            float angle = 0.0f;
            while (!form.IsDisposed)
            {
                if(form.WindowState!= FormWindowState.Minimized)
                {
                    angle += Time.DeltaTime;
                    M = glm.rotate(angle, new vec3(0,1,0)) * glm.scale(new mat4(1.0f), new vec3(2, 2, 2));
                    V = glm.lookAt(new vec3(5, 5, 5), new vec3(0, 0, 0), new vec3(0, 1, 0));
                    P = glm.perspective(glm.radians(45), (float)width / height, 0.1f, 100f);
                    MVP = P * V * M;
                    lightPos = new vec3(10,5,10);
                    Render();
                    graphicsBuffer.SwapBuffers();
                    Present();
                }
                Application.DoEvents();
                Time.Update();
            }
            graphicsBuffer.Dispose();
        }

        private void Present()
        {
            presentGraphics.DrawImage(graphicsBuffer.PresentGraphicsDevice.Bitmap, Point.Empty);
        }

        private void Render()
        {
            var device = graphicsBuffer.BackgroundGraphicsDevice;
            device.Clear(Color.White, 1.0f);
            //var pen = new Pen(Color.Red);
            //for (int i = 0; i < model.VertexCount; i += 3)
            //{
            //    vec4 p0 = MVP * new vec4(model.GetVertex(i).pos, 1.0f);
            //    vec4 p1 = MVP * new vec4(model.GetVertex(i + 1).pos, 1.0f);
            //    vec4 p2 = MVP * new vec4(model.GetVertex(i + 2).pos, 1.0f);

            //    vec2 p0_ = Clip2Screen(ProjectionDivide(p0));
            //    vec2 p1_ = Clip2Screen(ProjectionDivide(p1));
            //    vec2 p2_ = Clip2Screen(ProjectionDivide(p2));

            //    device.DrawTriangle(p0_, p1_, p2_,
            //        (fragIn) =>
            //        {
            //            return Color.FromArgb(
            //                (int)(fragIn.screenPos.x / width * 255),
            //                (int)(fragIn.screenPos.y / height * 255),
            //                255);
            //        });
            //}
            RenderModel(device, model,
                (vert) =>
                {
                    VertShaderOutput o = new VertShaderOutput();
                    o.MU_Position = MVP * new vec4(vert.pos, 1.0f);
                    o.worldPos = M * new vec4(vert.pos, 1.0f);
                    o.worldNormal = glm.normalize(M * new vec4(vert.norm, 0.0f));
                    return o;
                },
                (fragIn) =>
                {
                    var lightDir = glm.normalize(lightPos - fragIn.worldPos);
                    var normal = glm.normalize(fragIn.worldNormal);
                    var diff = Math.Max(glm.dot(lightDir,normal), 0.0f);
                    var diffuse = new vec4(1.0f) * diff;
                    diffuse.w = 1.0f;
                    Color color = Extensions.Colorf(diffuse);
                    return color;
                });
            device.Graphics.DrawString($"FPS: {1.0 / Time.DeltaTime:f1}", font, Brushes.Black, 0, 0);
        }
      
        private void RenderModel(GraphicsDevice device, Model model,
            Func<Vertex, VertShaderOutput> vshader, Func<FragShaderInput, Color> fshader)
        {
            for (int i = 0; i < model.VertexCount; i += 3)
            {
                var v0 = vshader(model.GetVertex(i));
                var v1 = vshader(model.GetVertex(i + 1));
                var v2 = vshader(model.GetVertex(i + 2));
                device.DrawTriangle(Clip2Screen, v0, v1, v2, fshader);
            }
        }
    }

    public class GraphicsBuffer : IDisposable
    {
        public GraphicsDevice BackgroundGraphicsDevice { get; private set; }
        public GraphicsDevice PresentGraphicsDevice { get; private set; }

        public GraphicsBuffer(Form form)
        {
            BackgroundGraphicsDevice = new GraphicsDevice(form.Width,form.Height);
            PresentGraphicsDevice = new GraphicsDevice(form.Width,form.Height);
        }

        public void SwapBuffers()
        {
            var t = PresentGraphicsDevice;
            PresentGraphicsDevice = BackgroundGraphicsDevice;
            BackgroundGraphicsDevice = t;
        }

        public void Dispose()
        {
            BackgroundGraphicsDevice.Dispose();
            PresentGraphicsDevice.Dispose();
        }
    }

    public class GraphicsDevice : IDisposable
    {
        public Bitmap ZBuffer { get; private set; }
        public Bitmap Bitmap { get; private set; }
        public Graphics Graphics { get; private set; }
        public Graphics ZGraphics { get; private set; }

        public GraphicsDevice(int width, int height)
        {
            Bitmap = new Bitmap(width, height);
            ZBuffer = new Bitmap(width, height);
            Graphics = Graphics.FromImage(Bitmap);
            ZGraphics = Graphics.FromImage(ZBuffer);
        }

        public void Clear(Color color, float depth)
        {
            Graphics.Clear(color);
            ZGraphics.Clear(Extensions.ClipZ2Color(depth));
        }

        public void DrawPoint(int x, int y, Color color)
        {
            Bitmap.SetPixel(x, y, color);
        }

        public bool DepthTest(int x, int y, float z)
        {
            var d = Extensions.Color2ClipZ(ZBuffer.GetPixel(x, y));
            if (Extensions.Color2ClipZ(ZBuffer.GetPixel(x, y)) < z) return false;
            else ZBuffer.SetPixel(x, y, Extensions.ClipZ2Color(z));
            return true;
        }

        private static float _cross(vec2 l, vec2 r)
        {
            return l.x * r.y - r.x * l.y;
        }

        private vec4 ProjectionDivide(vec4 v)
        {
            return v / v.w;
        }

        public void DrawTriangle(
            vec2 p0, vec2 p1, vec2 p2,
            Func<FragShaderInput, Color> shader)
        {
            vec2 v01 = p1 - p0;
            vec2 v12 = p2 - p1;
            vec2 v20 = p0 - p2;
            vec2 start = new vec2();
            vec2 end = new vec2();
            start.x = Math.Max(Math.Min(Math.Min(p0.x, p1.x), p2.x), 0);
            start.y = Math.Max(Math.Min(Math.Min(p0.y, p1.y), p2.y), 0);
            end.x = Math.Min(Math.Max(Math.Max(p0.x, p1.x), p2.x), Bitmap.Width);
            end.y = Math.Min(Math.Max(Math.Max(p0.y, p1.y), p2.y),Bitmap.Height);

            for (int y = (int)Math.Floor(start.y); y < (int)Math.Ceiling(end.y); ++y)
                for (int x = (int)Math.Floor(start.x); x < (int)Math.Ceiling(end.x); ++x)
                {
                    vec2 p = new vec2(x + 0.5f, y + 0.5f);
                    if (_cross(v01, p - p0) < 0.0f
                        && _cross(v12, p - p1) < 0.0f
                        && _cross(v20, p - p2) < 0.0f)
                    {
                        DrawPoint(x, y,
                            shader(new FragShaderInput { screenPos = new vec2(x, y) }
                            ));
                    }
                }
        }

        public void DrawTriangle(
            Func<vec4, vec2> clip2Screen,
            VertShaderOutput v0, VertShaderOutput v1, VertShaderOutput v2,
            Func<FragShaderInput, Color> shader)
        {
            vec2 p0 = clip2Screen(ProjectionDivide(v0.MU_Position));
            vec2 p1 = clip2Screen(ProjectionDivide(v1.MU_Position));
            vec2 p2 = clip2Screen(ProjectionDivide(v2.MU_Position));
            vec2 v01 = p1 - p0;
            vec2 v12 = p2 - p1;
            vec2 v20 = p0 - p2;
            vec2 start = new vec2();
            vec2 end = new vec2();
            start.x = Math.Max(Math.Min(Math.Min(p0.x, p1.x), p2.x), 0);
            start.y = Math.Max(Math.Min(Math.Min(p0.y, p1.y), p2.y), 0);
            end.x = Math.Min(Math.Max(Math.Max(p0.x, p1.x), p2.x), Bitmap.Width);
            end.y = Math.Min(Math.Max(Math.Max(p0.y, p1.y), p2.y), Bitmap.Height);

            for (int y = (int)Math.Floor(start.y); y < (int)Math.Ceiling(end.y); ++y)
                for (int x = (int)Math.Floor(start.x); x < (int)Math.Ceiling(end.x); ++x)
                {
                    vec2 p = new vec2(x + 0.5f, y + 0.5f);
                    if (_cross(v01, p - p0) < 0.0f
                        && _cross(v12, p - p1) < 0.0f
                        && _cross(v20, p - p2) < 0.0f)
                    {
                        var w = TriangleInterpolation(p0, p1, p2, p);
                        var fi = new FragShaderInput();
                        fi.screenPos = p;
                        fi.MU_Position = w.x * v0.MU_Position + w.y * v1.MU_Position + w.z * v2.MU_Position;
                        fi.worldNormal = w.x * v0.worldNormal + w.y * v1.worldNormal + w.z * v2.worldNormal;
                        fi.worldPos = w.x * v0.worldPos + w.y * v1.worldPos + w.z * v2.worldPos;
                        if (DepthTest(x, y, fi.MU_Position.z/fi.MU_Position.w)) DrawPoint(x, y, shader(fi));
                    }
                }
        }

        public vec3 TriangleInterpolation(vec2 p1, vec2 p2, vec2 p3, vec2 p)
        {
            var tmp = (p2.y - p3.y) * (p1.x - p3.x) + (p3.x - p2.x) * (p1.y - p3.y);
            var w0 = (p2.y - p3.y) * (p.x - p3.x) + (p3.x-p2.x)*(p.y - p3.y);
            var w1 = (p3.y - p1.y) * (p.x - p3.x) + (p1.x-p3.x)*(p.y - p3.y);
            w0 /= tmp;
            w1 /= tmp;
            var w2 = 1.0f - w0 - w1;
            return new vec3(w0, w1, w2);
        }

        public void Dispose()
        {
            ZBuffer.Dispose();
            Bitmap.Dispose();
            Graphics.Dispose();
            ZGraphics.Dispose();
        }
    }

    public struct Vertex
    {
        public vec3 pos;
        public vec3 norm;
        public vec2 tex;
    }

    public class Model
    {
        public int VertexCount => positionIndices.Count;

        public Vertex GetVertex(int i)
        {
            return new Vertex
            {
                pos = vertexPositions[positionIndices[i]],
                norm = vertexNormals[normalIndices[i]],
                tex = vertexTexcoords[texcoordIndices[i]]
            };
        }

        public List<int> positionIndices = new List<int>();
        public List<int> normalIndices = new List<int>();
        public List<int> texcoordIndices = new List<int>();
        public List<vec3> vertexPositions;
        public List<vec3> vertexNormals;
        public List<vec2> vertexTexcoords;
        private static ObjLoaderFactory objLoaderFactory = new ObjLoaderFactory();
        public Model(string path)
        {
            var objLoader = objLoaderFactory.Create();
            var fileStream = new FileStream(path, FileMode.Open);
            var result = objLoader.Load(fileStream);
            vertexPositions = new List<vec3>(result.Vertices.Count);
            for (int i = 0; i < result.Vertices.Count; ++i)
            {
                vertexPositions.Add(
                    new vec3(result.Vertices[i].X, result.Vertices[i].Y, result.Vertices[i].Z));
            }
            vertexNormals = new List<vec3>(result.Normals.Count);
            for (int i = 0; i < result.Normals.Count; ++i)
            {
                vertexNormals.Add(
                    new vec3(result.Normals[i].X, result.Normals[i].Y, result.Normals[i].Z));
            }
            vertexTexcoords = new List<vec2>(result.Textures.Count);
            for (int i = 0; i < result.Textures.Count; ++i)
            {
                vertexTexcoords.Add(
                    new vec2(result.Textures[i].X, result.Textures[i].Y));
            }
            for (int i = 0; i < result.Groups.Count; ++i)
            {
                foreach (var face in result.Groups[i].Faces)
                {
                    if(face.Count == 3)
                    {
                        positionIndices.Add(face[0].VertexIndex - 1);
                        positionIndices.Add(face[1].VertexIndex - 1);
                        positionIndices.Add(face[2].VertexIndex - 1);

                        normalIndices.Add(face[0].NormalIndex - 1);
                        normalIndices.Add(face[1].NormalIndex - 1);
                        normalIndices.Add(face[2].NormalIndex - 1);

                        texcoordIndices.Add(face[0].TextureIndex - 1);
                        texcoordIndices.Add(face[1].TextureIndex - 1);
                        texcoordIndices.Add(face[2].TextureIndex - 1);
                    }
                    else
                    {
                        positionIndices.Add(face[0].VertexIndex - 1);
                        positionIndices.Add(face[1].VertexIndex - 1);
                        positionIndices.Add(face[2].VertexIndex - 1);

                        normalIndices.Add(face[0].NormalIndex - 1);
                        normalIndices.Add(face[1].NormalIndex - 1);
                        normalIndices.Add(face[2].NormalIndex - 1);

                        texcoordIndices.Add(face[0].TextureIndex - 1);
                        texcoordIndices.Add(face[1].TextureIndex - 1);
                        texcoordIndices.Add(face[2].TextureIndex - 1);


                        positionIndices.Add(face[0].VertexIndex - 1);
                        positionIndices.Add(face[2].VertexIndex - 1);
                        positionIndices.Add(face[3].VertexIndex - 1);

                        normalIndices.Add(face[0].NormalIndex - 1);
                        normalIndices.Add(face[2].NormalIndex - 1);
                        normalIndices.Add(face[3].NormalIndex - 1);

                        texcoordIndices.Add(face[0].TextureIndex - 1);
                        texcoordIndices.Add(face[2].TextureIndex - 1);
                        texcoordIndices.Add(face[3].TextureIndex - 1);
                    }
                }
                
            }
        }
    }

    public class VertShaderAttr
    {
        public Vertex v;
    }

    public class VertShaderOutput
    {
        public vec4 MU_Position;
        public vec3 worldNormal;
        public vec3 worldPos;
    }

    public class FragShaderInput
    {
        public vec4 MU_Position;
        public vec3 worldNormal;
        public vec3 worldPos;

        public vec2 screenPos;
    }

    public static class Extensions
    {
        public static Color Colorf(vec4 color)
        {
            if (color.x > 1.0f) color.x = 1.0f;
            if (color.y > 1.0f) color.y = 1.0f;
            if (color.z > 1.0f) color.z = 1.0f;
            if (color.w > 1.0f) color.w = 1.0f;
            return Color.FromArgb((int)(255 * color.w), (int)(255 * color.x), (int)(255 * color.y), (int)(255 * color.z));
        }

        public static Color Normal2Color(vec3 normal)
        {
            return Colorf(new vec4(normal * 0.5f + new vec3(0.5f), 1.0f));
        }

        public static Color ClipZ2Color(float depth)
        {
            return Color.FromArgb((int)(int.MaxValue * depth));
        }

        public static float Color2ClipZ(Color color)
        {
            return (float)(uint)color.ToArgb() / int.MaxValue;
        }
    }
}
