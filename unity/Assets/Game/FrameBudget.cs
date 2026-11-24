namespace Stabby.Game
{
    public struct FrameBudget
    {
        public const int CreatureEntities = 768;

        public int Limit, Spent;

        public FrameBudget(int limit)
        {
            Limit = limit;
            Spent = 0;
        }

        public bool Take(int cost)
        {
            if (Spent > 0 && Spent + cost > Limit) return false;
            Spent += cost;
            return true;
        }
    }
}
