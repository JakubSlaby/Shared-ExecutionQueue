﻿using System.Collections.Generic;
using System.Threading;
using UnityEngine;
using WhiteSparrow.Shared.Queue.Items;

namespace WhiteSparrow.Shared.Queue
{
	public abstract class AbstractExecutionQueue<T> : IQueueItem
		where T : class, IQueueItem
	{
		#region Status API
		
		internal QueueResult m_Result = QueueResult.None;
		public QueueResult Result => m_Result;

		internal QueueState m_State = QueueState.None;
		public QueueState State => m_State;

		public bool IsRunning => State == QueueState.Running || State == QueueState.Stopping;
		public bool IsDone => State == QueueState.Completed || State == QueueState.Stopped;

		private int m_MaxConcurrentItems = 1;
		public int MaxConcurrentItems
		{
			get => m_MaxConcurrentItems;
			set => m_MaxConcurrentItems = Mathf.Max(1, value);
		}
		
		public object UserData { get; set; }

		/// <summary>
		/// When set to TRUE, will automatically start after any item is added, even after completing the items currently in queue. Default: FALSE
		/// Automatic start does not work with Execution Command overwrite.
		/// </summary>
		public bool AutomaticStart { get; set; } = false;
		
		#endregion

		#region Items API

		public int ItemCount => m_allItems.Count;

		public T[] GetItems()
		{
			if (m_allItemsCache == null)
				m_allItemsCache = m_allItems.ToArray();
			return m_allItemsCache;
		}
		
		public TItem Add<TItem>(TItem item)
			where TItem : class, T
		{
			if (item == null)
				return null;
			
			m_allItems.Add(item);
			m_allItemsCache = null;
			m_iteratedItems = null;
			
			OnItemAdded(item);
			
			if(AutomaticStart && State != QueueState.Running)
				_StartFromAutomatic();

			return item;
		}
		
		protected virtual void OnItemAdded(T item)
		{
			
		}

		public void Remove(T item)
		{
			if (item == null)
				return;

			if (!m_allItems.Contains(item))
				return;

			m_allItems.Remove(item);

			if (m_currentItems.Contains(item))
			{
				m_currentItems.Remove(item);
				item.OnComplete -= OnItemComplete;
			}
			
			m_allItemsCache = null;
			m_iteratedItems = null;

			OnItemRemoved(item);
		}

		protected virtual void OnItemRemoved(T item)
		{
			
		}
		
		
		#endregion

		#region Execution API

		public IQueueItem Start()
		{
			_Start();
			return this;
		}

		public void Stop()
		{
			_Stop();
		}

		public IQueueItem WithCancellation(CancellationToken token)
		{
			token.Register(OnExternalCancellationInvoked);
			return this;
		}

		private void OnExternalCancellationInvoked()
		{
			Stop();
		}

		#endregion
		
		
		#region Internal

		// All items added to this queue
		private List<T> m_allItems = new List<T>();
		
		// Cached list of all items used for external API getter
		private T[] m_allItemsCache;
		
		// Currently running items
		private HashSet<T> m_currentItems = new HashSet<T>();

		// Items that we are iterating through, there might be use cases where we
		// would like to filter out items that we run the queue on
		private T[] m_iteratedItems;

		private void _Start()
		{
			if (m_State != QueueState.None)
			{
				Debug.LogWarning("Queue already running");
				return;
			}
			
			m_State = QueueState.Running;
			
			m_iteratedItems = EvaluateIteratedItems();

			OnQueueStart();
			ProcessQueue();
		}

		private void _StartFromAutomatic()
		{
			if(m_State == QueueState.Running || m_State == QueueState.Stopping)
				return;
			
			m_State = QueueState.Running;
			
			m_iteratedItems = EvaluateIteratedItems();

			OnQueueStart();
			ProcessQueue();
			
		}

		protected virtual void OnQueueStart()
		{
		}


		private void ProcessQueue()
		{
			if (m_State != QueueState.Running)
				return;

			if (m_iteratedItems == null)
				m_iteratedItems = EvaluateIteratedItems();
			
			while (CanStartItem() && m_State != QueueState.Stopping)
			{
				T nextItem = EvaluateNextItem();
				if (nextItem == null)
					break;
				
				if(nextItem.State == QueueState.None)
					StartItem(nextItem);

				if (nextItem.State == QueueState.Running)
				{
					m_currentItems.Add(nextItem);
					nextItem.OnComplete += OnItemComplete;
				}
			}

			// we still have things ongoing
			if (m_currentItems.Count > 0)
				return;

			// abort iteration when we're stopping the queue
			if (m_State == QueueState.Stopping)
				return;
			
			QueueResult result = VerifyComplete();
			if (result == QueueResult.None)
				return;

			m_Result = result;
			m_State = QueueState.Completed;
			
			m_onComplete?.Invoke(this);
		}

		protected virtual T[] EvaluateIteratedItems()
		{
			return m_allItems.ToArray();
		}

		protected virtual bool CanStartItem()
		{
			return m_currentItems.Count < m_MaxConcurrentItems;
		}

		protected virtual T EvaluateNextItem()
		{
			for (int i = 0; i < m_iteratedItems.Length; i++)
			{
				var item = m_iteratedItems[i];
				if (item.State == QueueState.None)
					return item;
			}

			return null;
		}
		
		protected virtual void StartItem(T item)
		{
			item.Start();
		}
		
		private void OnItemComplete(IQueueItem item)
		{
			item.OnComplete -= OnItemComplete;

			if (item is T itemT)
			{
				m_currentItems.Remove(itemT);
				OnCompletedItem(itemT);
			}

			if (m_State == QueueState.Stopping)
			{
				ProcessStop();
			}
			else
			{
				ProcessQueue();
			}
		}

		protected virtual void OnCompletedItem(T item)
		{
			
		}

		private QueueResult VerifyComplete()
		{
			for (int i = 0; i < m_iteratedItems.Length; i++)
			{
				switch (m_iteratedItems[i].Result)
				{
					case QueueResult.Fail:
						return QueueResult.Fail;
					case QueueResult.None:
						return QueueResult.None;
				}
			}

			return QueueResult.Success;
		}

		private List<T> m_StopList;
		private void _Stop()
		{
			if (m_State != QueueState.Running)
				return;

			m_State = QueueState.Stopping;
			
			if(m_StopList == null)
				m_StopList = new List<T>();
			else
				m_StopList.Clear();
			m_StopList.AddRange(m_currentItems);
			
			foreach (var currentItem in m_StopList)
			{
				currentItem.Stop();
			}
		}

		private void ProcessStop()
		{
			if (m_currentItems.Count > 0)
				return;

			m_Result = QueueResult.Stop;
			m_State = QueueState.Stopped;

			if (AutomaticStart)
			{
				for (int i = m_allItems.Count; i >= 0; i--)
				{
					if(m_allItems[i].State != QueueState.None)
						continue;
					
					_StartFromAutomatic();
					return;
				}
			}
			
			m_onComplete?.Invoke(this);
		}
		
		#endregion
		
		private QueueItemDelegate m_onComplete;
		public event QueueItemDelegate OnComplete
		{
			add
			{
				if (IsDone)
				{
					value(this);
				}
				m_onComplete += value;
			}
			remove => m_onComplete -= value;
		}

		~AbstractExecutionQueue()
		{
			Dispose();
		}
		
		private bool m_Dispose;
		public void Dispose()
		{
			if (m_Dispose)
				return;
			m_Dispose = true;
			OnDispose();
			
			UserData = null;
			m_onComplete = null;

			var items = m_allItems.ToArray();
			m_allItems.Clear();
			m_allItems = null;
			m_allItemsCache = null;
			foreach (var item in items)
			{
				item.Dispose();
			}
		}

		protected virtual void OnDispose()
		{
			
		}
	}
}