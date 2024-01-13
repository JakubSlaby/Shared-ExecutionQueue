using UnityEngine.AddressableAssets;
using UnityEngine.ResourceManagement.AsyncOperations;
using UnityEngine.ResourceManagement.ResourceProviders;
using UnityEngine.SceneManagement;
using WhiteSparrow.Shared.Queue.Items;

namespace Plugins.WhiteSparrow.Queue.LoadQueue
{
	public class LoadAddressableSceneQueueItem : LoadAddressableQueueItem<SceneInstance>
	{
		private bool m_IsLoadOperation;
		private LoadSceneMode m_LoadMode;

		
		public LoadAddressableSceneQueueItem(AssetReference assetReference) : base(assetReference)
		{
		}

		public LoadAddressableSceneQueueItem(ICustomAddressableReference assetReference) : base(assetReference)
		{
		}

		public Scene SceneAsset
		{
			get
			{
				#if UNITY_EDITOR
				
				#endif
				return Asset.Scene;
			}
		}
		
		protected override AsyncOperationHandle<SceneInstance> TriggerAssetLoad(AssetReference assetReference)
		{
			m_IsLoadOperation = true;
			return Addressables.LoadSceneAsync(assetReference, m_LoadMode, true);
		}

		protected AsyncOperationHandle<SceneInstance> m_unloadHandle;
		protected override void ExecuteUnload()
		{
			if (m_IsLoadOperation && LoadOperation.IsValid())
			{
				CancelLoad();
			}
			m_IsLoadOperation = false;

			if (LoadOperation.IsValid())
			{
				m_unloadHandle = Addressables.UnloadSceneAsync(LoadOperation);
			}
			else
			{
				End(QueueResult.Success);
				return;
			}

			if (m_unloadHandle.IsDone)
			{
				End(QueueResult.Success);
				return;
			}

			m_unloadHandle.Completed += SceneUnloadCompleted;
		}

		private void SceneUnloadCompleted(AsyncOperationHandle<SceneInstance> obj)
		{
			m_unloadHandle.Completed -= SceneUnloadCompleted;
			End(QueueResult.Success);
		}
	}
}