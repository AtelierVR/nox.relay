using Nox.Controllers;

namespace Nox.Relay.Runtime.Players
{
    public class Part
    {
        public Player _player;
        public ushort _rig;

        public Part(Player player, ushort rig)
        {
            _player = player;
            _rig = rig;
        }

        public void Restore(IController controller)
        {
            throw new System.NotImplementedException();
        }

        public void Store()
        {
            throw new System.NotImplementedException();
        }
    }
}