using System;

public class Actions
{
    // public static Action<PlayerConstructor> OnPlayerAdded;
    // public static Action<ulong> OnPlayerRemoved;
    public static Action<PlayerConstructor> OnDummyAdded;
    public static Action<ulong> OnDummyRemoved;

    // Hand animation events
    public static Action<PlayerConstructor, bool> OnHandOpen; // PlayerConstructor, isLeftHand
    public static Action<PlayerConstructor, bool> OnHandClose; // PlayerConstructor, isLeftHand
}
