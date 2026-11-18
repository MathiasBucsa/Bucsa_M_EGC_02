using OpenTK.Mathematics;
using OpenTK.Windowing.GraphicsLibraryFramework;

namespace Egc;

/// <summary>
/// O masina din forme simple (cutii pentru caroserie, cilindri pentru roti), condusa cu sagetile,
/// cu o sfera care orbiteaza in jurul ei. Masina este o ierarhie: radacina (pivot) poarta pozitia
/// si directia, iar caroseria, rotile si orbita sunt copiii ei. Toate animatiile folosesc doar dt.
/// </summary>
public sealed class CarScene : Scene
{
    private const string ResetCar = "masina la start";

    private const float MaxSpeed = 7.0f;          // unitati / s, inainte
    private const float MaxReverse = 3.5f;        // unitati / s, inapoi
    private const float Acceleration = 3.0f;      // cat de repede se apropie viteza de tinta
    private const float TurnRate = 110.0f;        // grade / s la viteza maxima
    private const float MaxSteerAngle = 28.0f;    // cat se rotesc rotile din fata, in grade
    private const float WheelRadius = 0.35f;
    private const float Limit = 11.0f;            // limita zonei de joc, pe X si Z

    private SceneObject _root = null!;
    private readonly List<SceneObject> _spinPivots = new();    // rotesc rotile in jurul axei lor
    private readonly List<SceneObject> _steerPivots = new();   // directia rotilor din fata

    private Vector3 _position;
    private float _heading;        // directia masinii, in grade (pozitiv = spre stanga)
    private float _speed;          // pozitiv = inainte
    private float _roll;           // unghiul de rulare al rotilor, in grade
    private float _steerVisual;    // unghiul curent al rotilor din fata

    public override string Name => "masina";

    public override string Status => $"viteza {_speed:F1}";

    public override IEnumerable<string> ActionNames => new[]
    {
        Actions.Forward, Actions.Back, Actions.Left, Actions.Right, ResetCar,
    };

    public override void Load(Input input)
    {
        CameraPosition = new Vector3(0.0f, 11.0f, 14.0f);
        CameraTarget = new Vector3(0.0f, 0.0f, 0.0f);

        input.Bind(Actions.Forward, Keys.Up, Keys.W);
        input.Bind(Actions.Back, Keys.Down, Keys.S);
        input.Bind(Actions.Left, Keys.Left, Keys.A);
        input.Bind(Actions.Right, Keys.Right, Keys.D);
        input.Bind(ResetCar, Keys.R);

        Mesh grid = AddMesh(Shapes.Grid(12, 1.0f, new Color4(0.30f, 0.33f, 0.38f, 1.0f)));
        Mesh cube = AddMesh(Shapes.ShadedCube());
        Mesh sphere = AddMesh(Shapes.ShadedSphere(1.0f, 32, 16));
        Mesh wheel = AddMesh(Shapes.ShadedCylinder(WheelRadius, 0.3f, 24));

        AddObject("podea", grid);
        BuildCar(cube, wheel, sphere);
        Reset();
    }

    private void BuildCar(Mesh cube, Mesh wheelMesh, Mesh sphere)
    {
        // Fata masinii este spre -Z (in coordonatele ei locale).
        _root = AddObject("masina", null);

        SceneObject body = AddObject("caroserie", cube, _root);
        body.Transform.Position = new Vector3(0.0f, 0.6f, 0.0f);
        body.Transform.Scale = new Vector3(1.6f, 0.5f, 3.0f);
        body.Tint = new Color4(0.9f, 0.25f, 0.25f, 1.0f);

        SceneObject cabin = AddObject("cabina", cube, _root);
        cabin.Transform.Position = new Vector3(0.0f, 1.05f, 0.2f);
        cabin.Transform.Scale = new Vector3(1.3f, 0.45f, 1.5f);
        cabin.Tint = new Color4(0.55f, 0.75f, 1.0f, 1.0f);

        foreach (float x in new[] { -0.5f, 0.5f })
        {
            SceneObject light = AddObject("far", cube, _root);
            light.Transform.Position = new Vector3(x, 0.6f, -1.52f);
            light.Transform.Scale = new Vector3(0.3f, 0.2f, 0.05f);
            light.Tint = new Color4(1.0f, 0.9f, 0.3f, 1.0f);
        }

        // Fiecare roata: pivot de directie -> pivot de rulare -> cilindrul culcat pe o parte.
        foreach (float side in new[] { -1.0f, 1.0f })
        {
            foreach (float z in new[] { -1.0f, 1.0f })
            {
                SceneObject steer = AddObject("pivot directie", null, _root);
                steer.Transform.Position = new Vector3(side * 0.9f, WheelRadius, z);

                SceneObject spin = AddObject("pivot rulare", null, steer);

                SceneObject wheel = AddObject("roata", wheelMesh, spin);
                wheel.Transform.Rotation = new Vector3(0.0f, 0.0f, 90.0f);   // axa cilindrului pe X
                wheel.Tint = new Color4(0.1f, 0.1f, 0.12f, 1.0f);

                // O spita pe fata exterioara, ca rotirea rotii sa se vada.
                SceneObject spoke = AddObject("spita", cube, spin);
                spoke.Transform.Position = new Vector3(side * 0.17f, 0.0f, 0.0f);
                spoke.Transform.Scale = new Vector3(0.05f, 0.1f, 0.56f);
                spoke.Tint = new Color4(0.8f, 0.8f, 0.85f, 1.0f);

                _spinPivots.Add(spin);
                if (z < 0.0f) _steerPivots.Add(steer);
            }
        }

        // Sfera orbiteaza in jurul masinii: pivotul se roteste singur (Spin), sfera sta la distanta de el.
        SceneObject orbit = AddObject("orbita sferei", null, _root);
        orbit.Transform.Position = new Vector3(0.0f, 1.0f, 0.0f);
        orbit.Spin = new Vector3(0.0f, 120.0f, 0.0f);

        SceneObject ball = AddObject("sfera", sphere, orbit);
        ball.Transform.Position = new Vector3(2.4f, 0.3f, 0.0f);
        ball.Transform.Scale = new Vector3(0.35f);
        ball.Tint = new Color4(1.0f, 0.6f, 0.2f, 1.0f);
    }

    private void Reset()
    {
        _position = Vector3.Zero;
        _heading = 0.0f;
        _speed = 0.0f;
        _roll = 0.0f;
        _steerVisual = 0.0f;
        ApplyTransforms();
    }

    public override void Update(float dt, Input input)
    {
        base.Update(dt, input);   // Spin (orbita sferei)

        if (input.WasPressed(ResetCar))
        {
            Reset();
            return;
        }

        float throttle = input.Axis(Actions.Back, Actions.Forward);   // +1 inainte, -1 inapoi
        float steer = input.Axis(Actions.Right, Actions.Left);        // +1 stanga, -1 dreapta

        // Viteza se apropie lin de tinta; formula da acelasi rezultat la orice numar de cadre pe secunda.
        float target = throttle > 0.0f ? MaxSpeed : throttle < 0.0f ? -MaxReverse : 0.0f;
        _speed += (target - _speed) * (1.0f - MathF.Exp(-Acceleration * dt));

        // Masina vireaza doar cand se misca; mersul inapoi inverseaza singur sensul virajului.
        _heading += steer * TurnRate * (_speed / MaxSpeed) * dt;

        float radians = MathHelper.DegreesToRadians(_heading);
        var forward = new Vector3(-MathF.Sin(radians), 0.0f, -MathF.Cos(radians));
        _position += forward * _speed * dt;
        _position.X = Math.Clamp(_position.X, -Limit, Limit);
        _position.Z = Math.Clamp(_position.Z, -Limit, Limit);

        // Rotile: rulare proportionala cu viteza, directie netezita.
        _roll += _speed / WheelRadius * (180.0f / MathF.PI) * dt;
        _steerVisual += (steer * MaxSteerAngle - _steerVisual) * (1.0f - MathF.Exp(-10.0f * dt));

        ApplyTransforms();
    }

    private void ApplyTransforms()
    {
        _root.Transform.Position = _position;
        _root.Transform.Rotation = new Vector3(0.0f, _heading, 0.0f);

        foreach (SceneObject spin in _spinPivots)
        {
            spin.Transform.Rotation = new Vector3(-_roll, 0.0f, 0.0f);
        }
        foreach (SceneObject steer in _steerPivots)
        {
            steer.Transform.Rotation = new Vector3(0.0f, _steerVisual, 0.0f);
        }
    }

    public override IEnumerable<string> Describe()
    {
        foreach (string line in base.Describe()) yield return line;
        yield return $"Masina         : ({_position.X:F2}, {_position.Z:F2})   directia {_heading % 360.0f:F0} grade   viteza {_speed:F2}";
    }
}
