using Windows.Kinect;

public class HandEffects
{
    public void ManageHandEffects(PlayerConstructor player)
    {
        if (player.leftHandState == HandState.Open && player.leftHandStatePrev != HandState.Open)
        {
            player.leftHandVfx.SendEvent("handOpen");
        }
        if (player.rightHandState == HandState.Open && player.rightHandStatePrev != HandState.Open)
        {
            player.rightHandVfx.SendEvent("handOpen");
        }
        player.leftHandStatePrev = player.leftHandState;
        player.rightHandStatePrev = player.rightHandState;
    }
}
