using Assets._Project.Features.Movement;
using UnityEngine;
using UnityEngine.InputSystem;

public class PlayerController : MonoBehaviour
{
    [Header("MovementTypes")]
    public GroundedMovement GroundedMovement;

    [Header("Keybinds")]
    public InputActionReference MovementAction;
    
    private IMovement _activeIMovement;
    private Vector2 _latestDirection = Vector2.zero;

    public void FixedUpdate()
    {
        _activeIMovement.Move(_latestDirection);
    }

    private void Awake()
    {
        _activeIMovement = GroundedMovement;

        MovementAction.action.performed += OnMoved;
        MovementAction.action.canceled += OnMoved;
    }

    private void OnMoved(InputAction.CallbackContext context)
    {
        _latestDirection = context.ReadValue<Vector2>();
    }
}
