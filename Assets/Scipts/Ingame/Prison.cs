using System.Linq;

/* Brandt Gate's Prison: a set order with 監獄 ("Prison") in its name. A skill that imprisons puts
 the opponent's card in the Prison owner's order zone, so while a Prison is there any non-order
 card seen in that zone is imprisoned. Freeing one is calling it to its owner's rear-guard circle,
 which takes it out of the zone again. */
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
