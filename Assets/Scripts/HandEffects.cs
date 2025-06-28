using Windows.Kinect;

public class HandEffects
{
    public void ManageHandEffects(PlayerConstructor player)
    {
        if (player.leftHandState == HandState.Open && player.leftHandStatePrev != HandState.Open)
        {
            player.vfxAnimator.CrossFade(player.openClip.name, 1f);
            player.leftHandVfx.SendEvent("handOpen");
            player.vfxGraph.SendEvent("leftHandOpened");
        }
        else if (
            player.leftHandState == HandState.Closed
            && player.leftHandStatePrev != HandState.Closed
        )
        {
            player.vfxAnimator.CrossFade(player.closedClip.name, 1f);
            player.vfxGraph.SendEvent("leftHandClosed");
        }
        if (player.rightHandState == HandState.Open && player.rightHandStatePrev != HandState.Open)
        {
            player.vfxAnimator.CrossFade(player.openClipRight.name, 1f);
            player.rightHandVfx.SendEvent("handOpen");
            player.vfxGraph.SendEvent("rightHandOpened");
        }
        else if (
            player.rightHandState == HandState.Closed
            && player.rightHandStatePrev != HandState.Closed
        )
        {
            player.vfxAnimator.CrossFade(player.closedClipRight.name, 1f);
            player.vfxGraph.SendEvent("rightHandClosed");
        }
        player.leftHandStatePrev = player.leftHandState;
        player.rightHandStatePrev = player.rightHandState;
    }
}
