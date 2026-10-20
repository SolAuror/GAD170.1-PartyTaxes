using UnityEngine.InputSystem;
public partial class SimpleControls
{
    public interface IPlayerActions
    {
        void OnAttack(InputAction.CallbackContext context);
        void OnEncounter(InputAction.CallbackContext context);
        void OnSleep(InputAction.CallbackContext context);
        void OnHeal(InputAction.CallbackContext context);
        void OnRecruit(InputAction.CallbackContext context);
        void OnRunAway(InputAction.CallbackContext context);
        void OnReset(InputAction.CallbackContext context);
        void OnBless(InputAction.CallbackContext context);
        void On_2(InputAction.CallbackContext context);
        void OnResurrect(InputAction.CallbackContext context);
        void On_4(InputAction.CallbackContext context);
        void On_5(InputAction.CallbackContext context);
    }
    public struct PlayerActions { public void SetCallbacks(IPlayerActions i) {} public void Enable() {} public void Disable() {} }
    public PlayerActions Player => default;
}
