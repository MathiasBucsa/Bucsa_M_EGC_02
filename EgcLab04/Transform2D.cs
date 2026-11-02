using OpenTK.Mathematics;

namespace Egc;

/// <summary>
/// Poziția, rotația și scalarea unui obiect din scenă. Din ele se construiește matricea model,
/// care duce vârfurile obiectului din object space în world space (secțiunea 5).
/// </summary>
public class Transform2D
{
    public Vector2 Position = Vector2.Zero;   // translația, în unități de lume
    public float Rotation = 0.0f;             // unghiul, în GRADE, pozitiv = sens trigonometric
    public Vector2 Scale = Vector2.One;       // factorii de scalare pe x și pe y
    public float Depth = 0.0f;                // coordonata z; contează doar în perspectivă (secțiunea 7.2)

    public Matrix4 ModelMatrix { 
        // TODO (T1): scalare, apoi rotație, apoi translație
        get
        {
            Matrix4 scale = Matrix4.CreateScale(Scale.X, Scale.Y, 1.0f);
            Matrix4 rotation = Matrix4.CreateRotationZ(MathHelper.DegreesToRadians(Rotation));
            Matrix4 translation = Matrix4.CreateTranslation(Position.X, Position.Y, Depth);

            // T1: daca translatia ar veni inaintea rotatiei, obiectul ar fi rotit in jurul originii lumii,
            // nu in jurul propriului centru: s-ar muta pe un arc de cerc in loc sa se roteasca pe loc.
            // OpenTK foloseste conventia row-vector (p * M), deci matricele se inmultesc in ordinea in care
            // se aplica: intai scalare, apoi rotatie, apoi translatie.
            return scale * rotation * translation;
        }

        }

    /// <summary>O copie independentă (folosită pentru readucerea scenei în starea inițială).</summary>
    public Transform2D Clone() => (Transform2D)MemberwiseClone();
}
