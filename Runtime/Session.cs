using Cysharp.Threading.Tasks;
using Nox.CCK.Properties;
using Nox.Controllers;
using Nox.Entities;
using Nox.Players;
using Nox.Sessions;
using UnityEngine.Events;

namespace Nox.Relay.Runtime {
	public sealed class Session : BaseEditablePropertyObject, ISession {
		public string                       Id           { get; }
		public IDimensions                  Dimensions   { get; }
		public IEntities                    Entities     { get; }
		public IPlayer                      MasterPlayer { get; set; }
		public IPlayer                      LocalPlayer  { get; set; }
		public IState                       State        { get; }
		public UniTask                      Dispose() {
			throw new System.NotImplementedException();
		}
		public UniTask                      OnSelect(ISession   old) {
			throw new System.NotImplementedException();
		}
		public UniTask                      OnDeselect(ISession @new) {
			throw new System.NotImplementedException();
		}
		public UnityEvent<IPlayer>          OnPlayerJoined         { get; }
		public UnityEvent<IPlayer>          OnPlayerLeft           { get; }
		public UnityEvent<IPlayer, IPlayer> OnAuthorityTransferred { get; }
		public UnityEvent<IPlayer, bool>    OnPlayerVisibility     { get; }
		public UnityEvent<IEntity>          OnEntityRegistered     { get; }
		public UnityEvent<IEntity>          OnEntityUnregistered   { get; }
		public UnityEvent<IState>           OnStateChanged         { get; }

		public void OnControllerChanged(IController controller) {
			throw new System.NotImplementedException();
		}
	}
}