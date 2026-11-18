// Varianta B: evita obstacolele
using OpenTK.Mathematics;
using OpenTK.Windowing.GraphicsLibraryFramework;

namespace Egc;

/// <summary>
/// Tema, varianta B: un drum cu trei benzi. Jucatorul sta pe loc, obstacolele vin spre el.
/// Stanga / dreapta muta jucatorul pe banda vecina (Tween cu EaseOut), Space = salt cu gravitatie.
/// Obstacolele joase se pot sari, cele inalte se ocolesc. Coliziunea se testeaza cu cutii AABB.
/// Toate animatiile folosesc doar timpul de joc (dt), deci pauza si scara timpului merg.
/// </summary>
public sealed class ObstacleScene : Scene
{
    // Actiunea proprie scenei.
    private const string NewGame = "joc nou";

    // Drumul.
    private const int Lanes = 3;
    private const float LaneWidth = 2.0f;
    private const float PlayerZ = 3.0f;
    private const float SpawnZ = -48.0f;
    private const float RemoveZ = PlayerZ + 8.0f;

    // Jucatorul.
    private const float PlayerSize = 0.8f;
    private const float PlayerHalf = PlayerSize / 2.0f;
    private const float GroundY = PlayerHalf;        // centrul cubului cand sta pe drum
    private const float Gravity = 30.0f;             // unitati / s^2
    private const float JumpSpeed = 11.0f;           // inaltime maxima = v0^2 / 2g = 2.0
    private const float LaneChangeTime = 0.15f;      // sub 0.2 s

    // Lumea: viteza si frecventa obstacolelor cresc in timp.
    private const float BaseSpeed = 10.0f;
    private const float SpeedGain = 0.3f;            // unitati / s, in fiecare secunda
    private const float MaxSpeed = 26.0f;
    private const float StartInterval = 1.1f;        // secunde intre doua valuri de obstacole
    private const float MinInterval = 0.5f;
    private const float RampTime = 60.0f;            // dupa cat timp se ajunge la valorile maxime

    // Obstacolele.
    private const float ObstacleWidth = 1.4f;
    private const float ObstacleDepth = 1.0f;
    private const float LowHeight = 0.6f;            // se poate sari
    private const float TallHeight = 2.2f;           // nu se poate sari, se ocoleste

    // Marcajele drumului (bonus).
    private const int DashCount = 15;
    private const float DashPeriod = 4.0f;
    private const float DashLength = 1.5f;

    private sealed class Obstacle
    {
        public SceneObject Obj = null!;
        public float X, Z;
        public Vector3 Half;                         // jumatatile cutiei pe cele trei axe
    }

    private readonly List<Obstacle> _obstacles = new();
    private readonly List<SceneObject> _dashes = new();
    private readonly Random _rng = new();

    private Mesh _cube = null!;

    // Ierarhia jucatorului: pivotul poarta pozitia si inclinarea, corpul si vizorul sunt copiii lui.
    private SceneObject _playerRoot = null!;
    private SceneObject _body = null!;
    private SceneObject _shadow = null!;

    // Mutarea pe banda: un Tween pe axa X.
    private Tween _laneTween = null!;
    private int _lane = 1;

    private float _y = GroundY;
    private float _verticalSpeed;

    private float _survived;                         // scorul: timpul supravietuit, in secunde
    private float _best;                             // recordul
    private float _speed = BaseSpeed;
    private float _spawnTimer;
    private bool _gameOver;

    private bool OnGround => _y <= GroundY + 1e-4f;
    private static float LaneX(int lane) => (lane - 1) * LaneWidth;

    public override string Name => "evita obstacolele";

    public override string Status => _gameOver
        ? $"GAME OVER: {_survived:F1} s (record {_best:F1} s), R = joc nou"
        : $"scor {_survived:F1} s | record {_best:F1} s | viteza {_speed:F1}";

    public override IEnumerable<string> ActionNames => new[]
    {
        Actions.Left, Actions.Right, Actions.Jump, NewGame,
    };

    public override void Load(Input input)
    {
        CameraPosition = new Vector3(0.0f, 4.5f, 9.5f);
        CameraTarget = new Vector3(0.0f, 0.8f, -3.0f);

        input.Bind(Actions.Left, Keys.A, Keys.Left);
        input.Bind(Actions.Right, Keys.D, Keys.Right);
        input.Bind(Actions.Jump, Keys.Space);
        input.Bind(NewGame, Keys.R);

        _cube = AddMesh(Shapes.ShadedCube());
        Mesh disk = AddMesh(Shapes.ShadedCylinder(0.5f, 0.02f, 24));

        BuildRoad();
        BuildPlayer(disk);

        // Tween-ul pentru schimbarea benzii: pornit si oprit lin, cu EaseOut.
        _laneTween = new Tween(Vector3.Zero, Vector3.Zero, LaneChangeTime, Easing.EaseOut);

        StartNewGame();
    }

    // --- Constructia scenei ------------------------------------------------------------------

    private void BuildRoad()
    {
        float roadWidth = Lanes * LaneWidth;

        SceneObject road = AddObject("drum", _cube);
        road.Transform.Position = new Vector3(0.0f, 0.0f, -22.0f);
        road.Transform.Scale = new Vector3(roadWidth, 0.02f, 62.0f);
        road.Tint = new Color4(0.22f, 0.23f, 0.27f, 1.0f);

        // Margini.
        foreach (float side in new[] { -1.0f, 1.0f })
        {
            SceneObject edge = AddObject("margine", _cube);
            edge.Transform.Position = new Vector3(side * (roadWidth / 2.0f + 0.1f), 0.02f, -22.0f);
            edge.Transform.Scale = new Vector3(0.2f, 0.06f, 62.0f);
            edge.Tint = new Color4(0.8f, 0.8f, 0.85f, 1.0f);
        }

        // Marcaje intre benzi: se deplaseaza odata cu lumea si dau senzatia de viteza.
        foreach (float x in new[] { -LaneWidth / 2.0f, LaneWidth / 2.0f })
        {
            for (int i = 0; i < DashCount; i++)
            {
                SceneObject dash = AddObject("marcaj", _cube);
                dash.Transform.Position = new Vector3(x, 0.02f, -51.0f + i * DashPeriod);
                dash.Transform.Scale = new Vector3(0.1f, 0.02f, DashLength);
                dash.Tint = new Color4(0.85f, 0.85f, 0.9f, 1.0f);
                _dashes.Add(dash);
            }
        }
    }

    private void BuildPlayer(Mesh disk)
    {
        _shadow = AddObject("umbra jucatorului", disk);
        _shadow.Tint = new Color4(0.05f, 0.05f, 0.08f, 1.0f);

        // Pivotul nu are forma; corpul si vizorul sunt copiii lui si il urmeaza.
        _playerRoot = AddObject("jucator", null);

        _body = AddObject("corp", _cube, _playerRoot);
        _body.Transform.Scale = new Vector3(PlayerSize);

        SceneObject visor = AddObject("vizor", _cube, _playerRoot);
        visor.Transform.Position = new Vector3(0.0f, 0.1f, -PlayerHalf - 0.02f);
        visor.Transform.Scale = new Vector3(0.5f, 0.2f, 0.05f);
        visor.Tint = new Color4(0.05f, 0.1f, 0.15f, 1.0f);
    }

    // --- Un joc nou ----------------------------------------------------------------------------

    private void StartNewGame()
    {
        foreach (Obstacle ob in _obstacles)
        {
            Objects.Remove(ob.Obj);
        }
        _obstacles.Clear();

        _survived = 0.0f;
        _speed = BaseSpeed;
        _spawnTimer = 1.5f;
        _gameOver = false;

        _lane = 1;
        _laneTween.From = Vector3.Zero;
        _laneTween.To = Vector3.Zero;
        _laneTween.Duration = LaneChangeTime;
        _laneTween.Restart();

        _y = GroundY;
        _verticalSpeed = 0.0f;

        for (int i = 0; i < _dashes.Count; i++)
        {
            Vector3 p = _dashes[i].Transform.Position;
            p.Z = -51.0f + (i % DashCount) * DashPeriod;
            _dashes[i].Transform.Position = p;
        }

        _body.Tint = new Color4(0.3f, 0.85f, 1.0f, 1.0f);
        UpdateVisuals();
    }

    // --- Actualizarea, la fiecare cadru ------------------------------------------------------

    public override void Update(float dt, Input input)
    {
        base.Update(dt, input);

        if (input.WasPressed(NewGame))
        {
            StartNewGame();
            ConsolePanel.PrintMessage("joc nou");
            return;
        }

        // In pauza (dt = 0) sau dupa coliziune nimic nu se mai misca.
        if (dt <= 0.0f || _gameOver) return;

        _survived += dt;
        _speed = MathF.Min(BaseSpeed + SpeedGain * _survived, MaxSpeed);

        // Input: apasari, nu taste tinute.
        if (input.WasPressed(Actions.Left) && _lane > 0) ChangeLane(-1);
        if (input.WasPressed(Actions.Right) && _lane < Lanes - 1) ChangeLane(+1);
        if (input.WasPressed(Actions.Jump) && OnGround) _verticalSpeed = JumpSpeed;

        _laneTween.Update(dt);

        // Gravitatia (Euler semi-implicit): intai viteza, apoi pozitia.
        if (!OnGround || _verticalSpeed > 0.0f)
        {
            _verticalSpeed -= Gravity * dt;
            _y += _verticalSpeed * dt;
            if (_y <= GroundY)
            {
                _y = GroundY;
                _verticalSpeed = 0.0f;
            }
        }

        MoveWorld(dt);
        Spawn(dt);
        UpdateVisuals();
    }

    private void ChangeLane(int direction)
    {
        _lane += direction;
        _laneTween.GoTo(new Vector3(LaneX(_lane), 0.0f, 0.0f), LaneChangeTime);
    }

    private void MoveWorld(float dt)
    {
        float step = _speed * dt;

        // Marcajele ies prin fata si reapar in spate.
        float loop = DashCount * DashPeriod;
        foreach (SceneObject dash in _dashes)
        {
            Vector3 p = dash.Transform.Position;
            p.Z += step;
            if (p.Z > PlayerZ + 6.0f) p.Z -= loop;
            dash.Transform.Position = p;
        }

        // Obstacolele vin spre jucator.
        for (int i = _obstacles.Count - 1; i >= 0; i--)
        {
            Obstacle ob = _obstacles[i];
            float previousZ = ob.Z;
            ob.Z += step;
            ob.Obj.Transform.Position = new Vector3(ob.X, ob.Half.Y, ob.Z);

            if (Hits(ob, previousZ))
            {
                GameOver();
                return;
            }

            if (ob.Z > RemoveZ)
            {
                Objects.Remove(ob.Obj);
                _obstacles.RemoveAt(i);
            }
        }
    }

    /// <summary>
    /// Coliziune AABB: cutiile se suprapun daca se suprapun pe toate cele trei axe.
    /// Pe Z folosim tot drumul parcurs de obstacol in acest cadru, ca sa nu poata "sari" prin jucator
    /// la un pas de timp mare (de exemplu dupa o blocare).
    /// </summary>
    private bool Hits(Obstacle ob, float previousZ)
    {
        float playerX = _laneTween.Value.X;

        bool overlapX = MathF.Abs(playerX - ob.X) < PlayerHalf + ob.Half.X;
        bool overlapY = MathF.Abs(_y - ob.Half.Y) < PlayerHalf + ob.Half.Y;

        float zMin = MathF.Min(previousZ, ob.Z) - ob.Half.Z;
        float zMax = MathF.Max(previousZ, ob.Z) + ob.Half.Z;
        bool overlapZ = zMax > PlayerZ - PlayerHalf && zMin < PlayerZ + PlayerHalf;

        return overlapX && overlapY && overlapZ;
    }

    private void GameOver()
    {
        _gameOver = true;
        if (_survived > _best) _best = _survived;
        _body.Tint = new Color4(1.0f, 0.3f, 0.3f, 1.0f);
        ConsolePanel.PrintMessage($"coliziune: scor {_survived:F2} s, record {_best:F2} s (R = joc nou)");
    }

    // --- Obstacolele noi -------------------------------------------------------------------------

    private void Spawn(float dt)
    {
        _spawnTimer -= dt;
        if (_spawnTimer > 0.0f) return;

        int lane = _rng.Next(Lanes);
        SpawnObstacle(lane, _rng.Next(2) == 0);

        // Dupa un timp apar uneori doua obstacole odata, dar mereu ramane o banda libera.
        if (_survived > 15.0f && _rng.NextDouble() < 0.25)
        {
            int other = (lane + 1 + _rng.Next(2)) % Lanes;
            SpawnObstacle(other, _rng.Next(2) == 0);
        }

        float t = MathF.Min(_survived / RampTime, 1.0f);
        _spawnTimer = Easing.Lerp(StartInterval, MinInterval, t);
    }

    private void SpawnObstacle(int lane, bool tall)
    {
        float height = tall ? TallHeight : LowHeight;

        SceneObject obj = AddObject(tall ? "obstacol inalt" : "obstacol jos", _cube);
        obj.Transform.Scale = new Vector3(ObstacleWidth, height, ObstacleDepth);
        obj.Tint = tall ? new Color4(0.9f, 0.25f, 0.25f, 1.0f) : new Color4(1.0f, 0.65f, 0.2f, 1.0f);

        var ob = new Obstacle
        {
            Obj = obj,
            X = LaneX(lane),
            Z = SpawnZ,
            Half = new Vector3(ObstacleWidth / 2.0f, height / 2.0f, ObstacleDepth / 2.0f),
        };
        obj.Transform.Position = new Vector3(ob.X, ob.Half.Y, ob.Z);
        _obstacles.Add(ob);
    }

    // --- Afisarea starii jucatorului -------------------------------------------------------------

    private void UpdateVisuals()
    {
        float x = _laneTween.Value.X;

        _playerRoot.Transform.Position = new Vector3(x, _y, PlayerZ);

        // Se inclina in sensul mutarii, cat timp nu a ajuns pe banda tinta.
        float lean = -(LaneX(_lane) - x) * 12.0f;
        _playerRoot.Transform.Rotation = new Vector3(0.0f, 0.0f, lean);

        // Umbra ramane pe drum si se micsoreaza cu inaltimea.
        float height = _y - GroundY;
        float shadowSize = 1.0f / (1.0f + 0.25f * height);
        _shadow.Transform.Position = new Vector3(x, 0.02f, PlayerZ);
        _shadow.Transform.Scale = new Vector3(shadowSize, 1.0f, shadowSize);
    }

    public override IEnumerable<string> Describe()
    {
        foreach (string line in base.Describe()) yield return line;
        yield return $"Scor           : {_survived:F2} s   record: {_best:F2} s{(_gameOver ? "   GAME OVER" : "")}";
        yield return $"Lumea          : viteza {_speed:F1}   obstacole: {_obstacles.Count}   interval: {_spawnTimer:F2} s";
        yield return $"Jucător        : banda {_lane + 1}/{Lanes}   x = {_laneTween.Value.X:F2}   y = {_y:F2}   {(OnGround ? "pe sol" : "în aer")}";
    }
}
