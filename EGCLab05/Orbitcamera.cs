using OpenTK.Mathematics;
using OpenTK.Windowing.GraphicsLibraryFramework;

namespace Egc;

/// <summary>
/// Camera orbita: priveste mereu spre o tinta si se misca pe o sfera in jurul ei.
/// </summary>
public class OrbitCamera : Camera
{
    public Vector3 Target { get; private set; }
    public float Distance { get; private set; }
    public float Azimuth { get; private set; }      // grade
    public float Elevation { get; private set; }    // grade

    public const float MaxElevation = 89.0f;
    public const float MinDistance = 2.0f;
    public const float MaxDistance = 40.0f;
    public float MouseSensitivity = 0.3f;           // grade pe pixel
    public float KeySpeed = 60.0f;                  // grade pe secunda (sageti)
    public float ZoomStep = 0.8f;
    public float TargetSpeed = 4.0f;                // unitati pe secunda (W A S D)

    private static readonly Vector3 WorldUp = Vector3.UnitY;

    public OrbitCamera()
    {
        Reset();
    }

    public override string Name => "orbită";

    private Vector3 Offset
    {
        get
        {
            float az = MathHelper.DegreesToRadians(Azimuth);
            float el = MathHelper.DegreesToRadians(Elevation);
            return new Vector3(MathF.Cos(el) * MathF.Cos(az), MathF.Sin(el), MathF.Cos(el) * MathF.Sin(az));
        }
    }

    public override Vector3 Position => Target + Distance * Offset;

    public override Vector3 Front => -Offset;

    public override Matrix4 ViewMatrix => Matrix4.LookAt(Position, Target, WorldUp);

    public override void Update(KeyboardState keyboard, MouseState mouse, Vector2 mouseDelta, float dt)
    {
        if (mouse.IsButtonDown(MouseButton.Left))
        {
            Azimuth += mouseDelta.X * MouseSensitivity;
            Elevation += mouseDelta.Y * MouseSensitivity;
        }

        if (keyboard.IsKeyDown(Keys.Left)) Azimuth += KeySpeed * dt;
        if (keyboard.IsKeyDown(Keys.Right)) Azimuth -= KeySpeed * dt;
        if (keyboard.IsKeyDown(Keys.Up)) Elevation += KeySpeed * dt;
        if (keyboard.IsKeyDown(Keys.Down)) Elevation -= KeySpeed * dt;
        Elevation = Math.Clamp(Elevation, -MaxElevation, MaxElevation);

        Distance = Math.Clamp(Distance - mouse.ScrollDelta.Y * ZoomStep, MinDistance, MaxDistance);

        // W A S D muta tinta in plan orizontal
        Vector3 forward = new(Front.X, 0.0f, Front.Z);
        forward.Normalize();
        Vector3 right = Vector3.Cross(forward, WorldUp);

        Vector3 move = Vector3.Zero;
        if (keyboard.IsKeyDown(Keys.W)) move += forward;
        if (keyboard.IsKeyDown(Keys.S)) move -= forward;
        if (keyboard.IsKeyDown(Keys.D)) move += right;
        if (keyboard.IsKeyDown(Keys.A)) move -= right;
        if (move.LengthSquared > 0.0f)
        {
            Target += Vector3.Normalize(move) * TargetSpeed * dt;
        }
    }

    /// <summary>Preia pozitia si directia camerei FPS: tinta e la distanta data in fata ei.</summary>
    public void SetFromView(Vector3 position, Vector3 front, float distance)
    {
        front.Normalize();
        Vector3 o = -front;
        Target = position + front * distance;
        Distance = distance;
        Elevation = MathHelper.RadiansToDegrees(MathF.Asin(o.Y));
        Azimuth = MathHelper.RadiansToDegrees(MathF.Atan2(o.Z, o.X));
    }

    public override void Reset()
    {
        Target = new Vector3(0.0f, 1.0f, -6.0f);
        Distance = 14.0f;
        Azimuth = 90.0f;
        Elevation = 25.0f;
        FieldOfView = 60.0f;
    }

    public override IEnumerable<string> Describe()
    {
        Vector3 p = Position;
        yield return $"Țintă          : ({Target.X,6:F2}, {Target.Y,6:F2}, {Target.Z,6:F2})";
        yield return $"Distanță       : {Distance,6:F2}";
        yield return $"Azimut / elev. : {Azimuth,7:F1}° / {Elevation,6:F1}°";
        yield return $"Poziție        : ({p.X,6:F2}, {p.Y,6:F2}, {p.Z,6:F2})";
    }
}