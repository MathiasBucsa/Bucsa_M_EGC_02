using OpenTK.Graphics.OpenGL4;
using OpenTK.Mathematics;
using OpenTK.Windowing.Common;
using OpenTK.Windowing.Desktop;
using OpenTK.Windowing.GraphicsLibraryFramework;
using System.ComponentModel.DataAnnotations.Schema;

namespace Egc;

/// <summary>
/// Fereastra aplicației și bucla principală (main game loop).
/// Față de laboratorul 3: o scenă cu mai multe obiecte, fiecare cu matricea lui model,
/// o cameră cu matricele view și projection, tastatura și mouse-ul.
/// </summary>
public class Game : GameWindow
{
    // Obiecte care NU depind de OpenGL pot fi create imediat.
    private readonly Camera _camera = new();

    // Obiectele care folosesc OpenGL se creează în OnLoad (abia atunci există contextul OpenGL).
    private Shader _shader = null!;
    private readonly List<SceneObject> _scene = new();
    private readonly List<Transform2D> _initialTransforms = new();   // pentru tasta R

    // Starea aplicației (se modifică în OnUpdateFrame, se folosește în OnRenderFrame).
    private int _selected;            // indexul obiectului selectat în _scene (tasta Tab)
    private bool _wireframe;          // contur (true) sau plin (false)

    // Contoare afișate în titlul ferestrei.
    private readonly string _baseTitle;
    private double _secondTimer;
    private int _frames, _lastFrames;
    private bool _titleDirty = true;

    private Vector2 _pivot = Vector2.Zero;

    private SceneObject _satellite;
    private float _orbitAngle = 0.0f;


    public Game(int width, int height, string title)
        : base(GameWindowSettings.Default,
               new NativeWindowSettings
               {
                   ClientSize = new Vector2i(width, height),
                   Title = title,
                   APIVersion = new Version(3, 3),          // cerem OpenGL 3.3 ...
                   Profile = ContextProfile.Core,           // ... profil Core (fără modul imediat)
                   Flags = ContextFlags.ForwardCompatible
               })
    {
        _baseTitle = title;
        VSync = VSyncMode.On;   // swap-ul bufferelor așteaptă reîmprospătarea monitorului
    }

    /// <summary>Apelată O SINGURĂ DATĂ, după crearea ferestrei și a contextului OpenGL.</summary>
    protected override void OnLoad()
    {
        base.OnLoad();

        Console.WriteLine($"OpenGL  : {GL.GetString(StringName.Version)}");
        Console.WriteLine($"Renderer: {GL.GetString(StringName.Renderer)}");

        GL.Viewport(0, 0, ClientSize.X, ClientSize.Y);
        _camera.Resize(ClientSize.X, ClientSize.Y);

        _shader = Shader.FromFiles("Shaders/shader.vert", "Shaders/shader.frag");


        // Scena: aceleași forme din laboratorul 3, acum plasate în lume prin Transform2D.
        // Depth (z) contează doar în perspectivă (tasta O, secțiunea 7.2).
        AddObject("pătrat", Shapes.Quad(),
                  new Transform2D { Position = new Vector2(-1.8f, -1.0f), Depth = -0.3f});
        AddObject("triunghi", Shapes.Triangle(),
                  new Transform2D { Position = new Vector2(-1.2f, 1.0f) });
        AddObject("hexagon", Shapes.RegularPolygon(6, 0.5f),
                  new Transform2D { Position = new Vector2(2.0f, 1.0f) });
        AddObject("cerc", Shapes.RegularPolygon(64, 0.5f),
                  new Transform2D { Position = new Vector2(0.8f, -0.6f), Depth = 2.0f });

        //T5
        _satellite = new SceneObject("satelit", Shapes.Triangle(), new Transform2D());
    }

    /// <summary>Adaugă un obiect în scenă și reține transformarea lui inițială (pentru tasta R).</summary>
    private void AddObject(string name, Mesh mesh, Transform2D transform)
    {
        _scene.Add(new SceneObject(name, mesh, transform));
        _initialTransforms.Add(transform.Clone());
    }

    /// <summary>Apelată când se schimbă dimensiunea ferestrei.</summary>
    protected override void OnResize(ResizeEventArgs e)
    {
        base.OnResize(e);

        GL.Viewport(0, 0, e.Width, e.Height);   // zona ferestrei în care se desenează
        _camera.Resize(e.Width, e.Height);      // proiecția se adaptează la noul raport de aspect
    }

    /// <summary>Apelată la fiecare iterație a buclei: LOGICA aplicației (intrări, mișcare, timp).</summary>
    protected override void OnUpdateFrame(FrameEventArgs args)
    {
        base.OnUpdateFrame(args);

        if (KeyboardState.IsKeyDown(Keys.Escape))
        {
            Close();
        }

        // --- Obiectul selectat: tastatura (secțiunea 8) ------------------------------

        float dt = (float)args.Time;
        SceneObject sel = _scene[_selected];

        float boost = KeyboardState.IsKeyDown(Keys.LeftShift) ? 3.0f : 1.0f;

        const float rotSpeed = 90.0f;
        const float scaleRate = 1.5f;

        const float moveSpeed = 2.0f;   // unități de lume pe secundă
        if (KeyboardState.IsKeyDown(Keys.Left))  sel.Transform.Position.X -= moveSpeed * boost * dt;
        if (KeyboardState.IsKeyDown(Keys.Right)) sel.Transform.Position.X += moveSpeed * boost * dt;
        if (KeyboardState.IsKeyDown(Keys.Down))  sel.Transform.Position.Y -= moveSpeed * boost * dt;
        if (KeyboardState.IsKeyDown(Keys.Up))    sel.Transform.Position.Y += moveSpeed * boost * dt;

        // TODO (T2): Q / E rotesc obiectul, Z / X îl scalează

        if (KeyboardState.IsKeyDown(Keys.Q)) sel.Transform.Rotation += rotSpeed * boost * dt;
        if (KeyboardState.IsKeyDown(Keys.E)) sel.Transform.Rotation -= rotSpeed * boost * dt;

        // T2: scalarea se face prin inmultire, nu prin adunare. Prin adunare (de exemplu
        // -0,5 pe secunda) scara ar ajunge la 0 si apoi la valori negative, care oglindesc
        // forma. Prin inmultire, scara se apropie de 0 fara sa-l atinga, iar marirea si
        // micsorarea sunt simetrice (Z urmat de X readuce exact scara de la inceput).
        // Un factor f pe secunda, pe un cadru de durata dt, înseamna inmultirea cu f^dt.

        float factor = MathF.Pow(scaleRate, boost * dt);

        if (KeyboardState.IsKeyDown(Keys.X)) sel.Transform.Scale *= factor;   // mărire
        if (KeyboardState.IsKeyDown(Keys.Z)) sel.Transform.Scale /= factor;   // micșorare
        sel.Transform.Scale = Vector2.Clamp(sel.Transform.Scale, new Vector2(0.1f), new Vector2(10.0f));

        if (KeyboardState.IsKeyPressed(Keys.Tab)) _selected = (_selected + 1) % _scene.Count;
        if (KeyboardState.IsKeyPressed(Keys.O))   _camera.IsPerspective = !_camera.IsPerspective;
        if (KeyboardState.IsKeyPressed(Keys.F))   _wireframe = !_wireframe;
        if (KeyboardState.IsKeyPressed(Keys.R))   ResetScene();

        if (KeyboardState.IsKeyPressed(Keys.Tab) || KeyboardState.IsKeyPressed(Keys.O) ||
            KeyboardState.IsKeyPressed(Keys.R))
        {
            _titleDirty = true;
        }
        //T5

        _orbitAngle += 90.0f * dt;

        // --- Obiectul selectat: mouse-ul (secțiunea 9) -------------------------------

        // TODO (T3): la clic stânga, obiectul selectat se mută în punctul din lume aflat sub cursor
        //            (folosiți ScreenToWorld; Depth nu se schimbă)



        if (MouseState.IsButtonPressed(MouseButton.Left))
        {
            sel.Transform.Position = ScreenToWorld(MouseState.Position);
        }


        // TODO (T4, facultativ): clic dreapta = pivot; tasta P ținută = rotație în jurul pivotului

        if (MouseState.IsButtonPressed(MouseButton.Right))
        {
            _pivot = ScreenToWorld(MouseState.Position);
        }

        if (KeyboardState.IsKeyDown(Keys.P))
        {
            float angle = rotSpeed * boost * dt;

            Vector2 offset = sel.Transform.Position - _pivot;
            float rad = MathHelper.DegreesToRadians(angle);
            float cos = MathF.Cos(rad);
            float sin = MathF.Sin(rad);
            Vector2 rotated = new Vector2(offset.X * cos - offset.Y * sin, offset.X * sin + offset.Y * cos);
            sel.Transform.Position = _pivot + rotated;
            sel.Transform.Rotation += angle;
        }

        // --- Camera: tastatura și rotița (secțiunea 6.2) -----------------------------

        float camSpeed = 3.0f / _camera.Zoom;                    // unități de lume pe secundă
        if (KeyboardState.IsKeyDown(Keys.A)) _camera.Position.X -= camSpeed * dt;
        if (KeyboardState.IsKeyDown(Keys.D)) _camera.Position.X += camSpeed * dt;
        if (KeyboardState.IsKeyDown(Keys.S)) _camera.Position.Y -= camSpeed * dt;
        if (KeyboardState.IsKeyDown(Keys.W)) _camera.Position.Y += camSpeed * dt;

        _camera.Zoom *= MathF.Pow(1.1f, MouseState.ScrollDelta.Y);   // un pas al rotiței = ±10 %
        _camera.Zoom = Math.Clamp(_camera.Zoom, 0.1f, 10.0f);

        // --- Titlul ferestrei: obiectul selectat, proiecția, cadre pe secundă --------

        _secondTimer += args.Time;
        if (_secondTimer >= 1.0)
        {
            _lastFrames = _frames;
            _frames = 0;
            _secondTimer -= 1.0;
            _titleDirty = true;
        }

        if (_titleDirty)
        {
            string projection = _camera.IsPerspective ? "perspectivă" : "ortografică";
            Title = $"{_baseTitle} | selectat: {_scene[_selected].Name} | proiecție {projection} | FPS: {_lastFrames}";
            _titleDirty = false;
        }
    }

    /// <summary>Apelată la fiecare iterație a buclei: DESENAREA unui cadru.</summary>
    protected override void OnRenderFrame(FrameEventArgs args)
    {
        base.OnRenderFrame(args);
        _frames++;

        // 1. Ștergem imaginea veche din back buffer.
        GL.ClearColor(0.12f, 0.14f, 0.18f, 1.0f);
        GL.Clear(ClearBufferMask.ColorBufferBit);

        // 2. Starea de rasterizare și programul shader.
        SetWireframe(_wireframe);
        _shader.Use();

        // 3. View și projection sunt aceleași pentru toate obiectele: le trimitem o singură dată.
        _shader.SetMatrix4("uView", _camera.ViewMatrix);
        _shader.SetMatrix4("uProjection", _camera.ProjectionMatrix);

        // 4. Fiecare obiect: matricea lui model, nuanța, apoi desenarea.
        foreach (SceneObject obj in _scene)
        {
            _shader.SetMatrix4("uModel", obj.Transform.ModelMatrix);
            _shader.SetColor("uTint", obj.Tint);
            obj.Mesh.Draw();
        }

        // TODO (T5, facultativ): satelitul obiectului selectat

        Matrix4 local = Matrix4.CreateScale(0.3f, 0.3f, 1.0f) *
                        Matrix4.CreateTranslation(1.2f, 0.0f, 0.0f) *
                        Matrix4.CreateRotationZ(MathHelper.DegreesToRadians(_orbitAngle));

        Matrix4 parentModel = _scene[_selected].Transform.ModelMatrix;
        _shader.SetMatrix4("uModel", local * parentModel);
        _shader.SetColor("uTint", _satellite.Tint);
        _satellite.Mesh.Draw();

        // T5: cand obiectul selectat este scalat, orbita se scaleaza odata cu el. Matricea model a
        // parintelui contine scalarea si se aplica DUPA transformarea locala a satelitului, deci
        // inmulteste si distanta 1,2 (translatia locala) cu factorul de scara: raza orbitei devine
        // 1,2 * scara, iar satelitul se mareste si el cu acelasi factor (scara lui 0,3 devine 0,3 * scara).
        // La scalare neuniforma (sx != sy) orbita devine elipsa, pentru ca x si y sunt inmultite
        // cu factori diferiti.


        // 5. Afișăm cadrul terminat: back buffer <-> front buffer.
        SwapBuffers();
    }

    /// <summary>Apelată O SINGURĂ DATĂ, la închiderea ferestrei: eliberăm resursele GPU.</summary>
    protected override void OnUnload()
    {
        foreach (SceneObject obj in _scene)
        {
            obj.Dispose();
        }
        _shader.Dispose();

        base.OnUnload();
    }

    /// <summary>Transformă poziția mouse-ului (pixeli, origine stânga-sus) în coordonate de lume.</summary>
    private Vector2 ScreenToWorld(Vector2 mousePixels)
    {
        // TODO (T3): pasul 1 (pixel → NDC), apoi pasul 2 (NDC → lume, cu inversa lui view * projection)

        float xNdc = 2.0f * mousePixels.X / ClientSize.X - 1.0f;
        float yNdc = 1.0f - 2.0f * mousePixels.Y / ClientSize.Y;

        Matrix4 inverse = (_camera.ViewMatrix * _camera.ProjectionMatrix).Inverted();
        Vector4 world = new Vector4(xNdc, yNdc, 0.0f, 1.0f) * inverse;

        return new Vector2(world.X, world.Y);

        //apare " 1 - "  la y pentru ca axa y a mouse ului creste in jos ( origine stanga-sus), in timp 
        //in timp ce in NDC y creste in sus. Fara acest semn, la clic obiectul ar ajunge in punctul simetric fata de axa orizontala

    }

    /// <summary>Tasta R: obiectele revin la transformările inițiale, camera la starea inițială.</summary>
    private void ResetScene()
    {
        for (int i = 0; i < _scene.Count; i++)
        {
            _scene[i].Transform = _initialTransforms[i].Clone();
        }
        _camera.Reset();
    }

    /// <summary>
    /// Contur / plin. Dacă Visual Studio semnalează o eroare la primul parametru,
    /// folosiți aici tipul acceptat de versiunea voastră (vezi laboratorul 2, Problema 3):
    /// TriangleFace.FrontAndBack sau MaterialFace.FrontAndBack.
    /// </summary>
    private static void SetWireframe(bool wireframe)
    {
        GL.PolygonMode(TriangleFace.FrontAndBack, wireframe ? PolygonMode.Line : PolygonMode.Fill);
    }
}
