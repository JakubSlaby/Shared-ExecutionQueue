#if EXECUTIONQUEUE_ADRESSABLES
using Cysharp.Threading.Tasks;
using UnityEngine.AddressableAssets;
using UnityEngine.ResourceManagement.AsyncOperations;
using WhiteSparrow.Shared.Queue.Items;

namespace Plugins.WhiteSparrow.Queue.LoadQueue
{
	public delegate void LoadQueueItemDelegate<T>(LoadAddressableQueueItem<T> item);

	public abstract class AbstractLoadAddressableQueueItem : AbstractLoadQueueItem, ILoadAddressableAssetQueueItem
	{
		private AssetReference m_AssetReference;
		public AssetReference AssetReference => m_AssetReference;

		private ICustomAddressableReference m_CustomAssetReference;
		public ICustomAddressableReference CustomAssetReference => m_CustomAssetReference;
		
		public AbstractLoadAddressableQueueItem(AssetReference assetReference)
		{
			m_AssetReference = assetReference;
		}

		public AbstractLoadAddressableQueueItem(ICustomAddressableReference assetReference)
		{
			m_AssetReference = assetReference.AssetReference;
			m_CustomAssetReference = assetReference;
		}

		object ILoadAssetQueueItem.Asset => ResolveAsset();
		public object Asset => ResolveAsset();
		protected abstract object ResolveAsset();
	}
	
	public class LoadAddressableQueueItem<T> : AbstractLoadAddressableQueueItem
	{
		private LoadQueueItemDelegate<T> m_OnComplete;
		public new event LoadQueueItemDelegate<T> OnComplete
		{
			add
			{
				if (IsDone)
					value(this);
				else
					m_OnComplete += value;
			}
			remove => m_OnComplete -= value;
		}

		protected override void InvokeOnComplete()
		{
			m_OnComplete?.Invoke(this);
			m_OnComplete = null;
			base.InvokeOnComplete();
		}
		
		public LoadAddressableQueueItem(AssetReference assetReference) : base(assetReference)
		{
		}

		public LoadAddressableQueueItem(ICustomAddressableReference assetReference) : base(assetReference)
		{
		}

		public new T Asset
		{
			get
			{
				return m_LoadOperation.IsValid() ? (T) m_LoadOperation.Result : default;
			}
		}

		protected override object ResolveAsset() => this.Asset;

		private AsyncOperationHandle m_LoadOperation;
		protected AsyncOperationHandle LoadOperation => m_LoadOperation;
	
		
		

		protected override async UniTask ExecuteLoad()
		{
			if (AssetReference == null)
			{
				SetResult(QueueResult.Fail);
				return;
			}

			if (AssetReference.OperationHandle.IsValid())
			{
				m_LoadOperation = AssetReference.OperationHandle;
				await m_LoadOperation;

			}
			else
			{
				m_LoadOperation = TriggerAssetLoad(AssetReference);
				await m_LoadOperation.WithCancellation(CancellationToken);
			}


			if (m_LoadOperation.OperationException != null)
			{
				SetResult(QueueResult.Fail);
				return;
			}
			
			await OnLoadOperationCompleted(m_LoadOperation);
		}

		protected virtual AsyncOperationHandle<T> TriggerAssetLoad(AssetReference assetReference)
		{
			return AssetReference.LoadAssetAsync<T>();
		}

		private UniTask OnLoadOperationCompleted(AsyncOperationHandle loadOperation)
		{
			if (loadOperation.OperationException != null)
			{
				SetResult(QueueResult.Fail);
				return UniTask.CompletedTask;
			}

			if (loadOperation.Status == AsyncOperationStatus.Failed)
			{
				SetResult(QueueResult.Fail);
			}
			
			Addressables.ResourceManager.Acquire(m_LoadOperation);
			return UniTask.CompletedTask;
		}




		protected virtual void CancelLoad()
		{
			if (m_LoadOperation.IsValid())
			{
				Addressables.Release(m_LoadOperation);
			}
			
		}
		
		protected override async UniTask ExecuteUnload()
		{
			CancelLoad();
			SetResult(QueueResult.Success);
		}

	}

	public interface ICustomAddressableReference
	{
		AssetReference AssetReference { get; }
	}
}
#endif