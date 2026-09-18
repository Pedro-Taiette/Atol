using Assets._Project.Features.Movement;
using Unity.VisualScripting;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.Splines.Interpolators;

public class GroundedMovement : MonoBehaviour, IMovement
{
    public Transform ReferenceTransform;
    public CharacterController Controller;

    public float Speed = 6f;
    public float JumpHeight = 2f;
    public float GravityMultiplier = 1f;
    public float TurnSmoothTime = 0.1f;
    public float Acceleration = 20f;
    public float Deceleration = 15f;

    private float _turnSmoothVelocity;

    private Vector3 _direction;
    private Vector3 _velocity;

    public void Move(Vector2 inputDirection)
    {
        _direction = new Vector3(inputDirection.x, 0f, inputDirection.y).normalized;
    }

    public void Jump(bool released)
    {
        if (!Controller.isGrounded || released)
            return;

        _velocity.y += Mathf.Sqrt(JumpHeight * -2f * Physics.gravity.y);
    }
    public void ApplyMovement()
    {
        Vector3 targetHorizontalVelocity = Vector3.zero;

        if (_direction.sqrMagnitude > 0f)
        {
            if (ReferenceTransform)
            {
                float targetAngle = Mathf.Atan2(_direction.x, _direction.z) * Mathf.Rad2Deg + ReferenceTransform.eulerAngles.y;

                SmoothApplyRotation(targetAngle);

                Vector3 recalculatedDirection = Quaternion.Euler(0f, targetAngle, 0f) * Vector3.forward;

                targetHorizontalVelocity = recalculatedDirection * Speed;
            }
            else
            {
                float targetAngle = Mathf.Atan2(_direction.x, _direction.z) * Mathf.Rad2Deg;

                SmoothApplyRotation(targetAngle);

                targetHorizontalVelocity = _direction * Speed;
            }
        }

        Vector3 currentHorizontalVelocity = new Vector3(_velocity.x, 0f, _velocity.z);

        float rate = targetHorizontalVelocity.sqrMagnitude > 0f
            ? Acceleration
            : Deceleration;

        currentHorizontalVelocity = Vector3.MoveTowards(
            currentHorizontalVelocity,
            targetHorizontalVelocity,
            rate * Time.deltaTime
        );

        _velocity.x = currentHorizontalVelocity.x;
        _velocity.z = currentHorizontalVelocity.z;

        ApplyGravity();

        Controller.Move(_velocity * Time.deltaTime);
    }
    private void ApplyGravity()
    {
        if (Controller.isGrounded && _velocity.y < 0f)
        {
            _velocity.y = -1f;
        }
        else
        {
            _velocity.y += Physics.gravity.y * GravityMultiplier * Time.deltaTime;
        }
    }

    private void SmoothApplyRotation(float targetAngle)
    {
        float angle = Mathf.SmoothDampAngle(transform.eulerAngles.y, targetAngle, ref _turnSmoothVelocity, TurnSmoothTime);

        transform.rotation = Quaternion.Euler(0f, angle, 0f);
    }
}
