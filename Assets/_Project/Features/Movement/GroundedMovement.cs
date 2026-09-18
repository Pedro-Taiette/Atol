using Assets._Project.Features.Movement;
using UnityEngine;

public class GroundedMovement : MonoBehaviour, IMovement
{
    public Transform ReferenceTransform;
    public CharacterController Controller;

    public float Speed = 6f;
    public float TurnSmoothTime = 0.1f;
    private float _turnSmoothVelocity;

    public void Move(Vector2 inputDirection)
    {
        Vector3 direction = new Vector3(inputDirection.x, 0f, inputDirection.y).normalized;

        if (direction.magnitude == 0f)
            return;

        if (ReferenceTransform) // Movement in relation to a transform
        {
            float targetAngle = Mathf.Atan2(direction.x, direction.z) * Mathf.Rad2Deg + ReferenceTransform.eulerAngles.y;

            float angle = Mathf.SmoothDampAngle(transform.eulerAngles.y, targetAngle, ref _turnSmoothVelocity, TurnSmoothTime);

            transform.rotation = Quaternion.Euler(0f, angle, 0f);

            Vector3 movementDirection = Quaternion.Euler(0f, targetAngle, 0f) * Vector3.forward;

            Controller.Move(Speed * Time.deltaTime * movementDirection.normalized); 
        }
        else // Movement related to world
        {
            float targetAngle = Mathf.Atan2(direction.x, direction.z) * Mathf.Rad2Deg;

            float angle = Mathf.SmoothDampAngle(transform.eulerAngles.y, targetAngle, ref _turnSmoothVelocity, TurnSmoothTime);

            transform.rotation = Quaternion.Euler(0f, angle, 0f);

            Controller.Move(Speed * Time.deltaTime * direction); 
        }
    }
}
