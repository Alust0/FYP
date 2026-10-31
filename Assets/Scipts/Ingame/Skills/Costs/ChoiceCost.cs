using System.Collections.Generic;
using System.Linq;

// "A or B", e.g. [COST][Counter Blast (2) or put a card from hand into your soul]: the player picks one (CostStep)
public class ChoiceCost : Cost
{
    public IReadOnlyList<Cost> Options { get; }

    public ChoiceCost(List<Cost> options) : base(1)
    {
        Options = options;
    }

    public override string Name => string.Join(" or ", Options.Select(o => o.ToString()));
    public override string ShortText => string.Join(" / ", Options.Select(o => o.ShortText));

    public override bool CanPay(PlayerState owner, CardInstance card) => Options.Any(o => o.CanPay(owner, card));

    // Paid through the option picked
    public override void Pay(PlayerState owner, CardInstance card) { }

    public override string ToString() => Name;
}
