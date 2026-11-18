using System.Linq;

// Brandt Gate's Prison order
public static class Prison
{
    public static bool IsPrison(CardData data) =>
        data != null && data.isOrderCard && (CardFilter.Has(data.cardName, "監獄") || CardFilter.Has(data.cardNameEn, "Prison"));

    // The player has a Prison in their order zone
    public static bool Has(PlayerState player) => player != null && player.orderZone.Any(c => IsPrison(c.data));

    // The opponent's cards imprisoned in this player's Prison, for "if ... are imprisoned in your Prison"
    public static int Imprisoned(PlayerState player) =>
        Has(player) ? player.orderZone.Count(c => !c.data.isOrderCard) : 0;
}
