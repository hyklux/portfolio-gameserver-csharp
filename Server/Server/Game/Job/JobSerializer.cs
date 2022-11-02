using System;
using System.Collections.Generic;
using System.Text;

namespace Server.Game
{
	//패킷 핸들러 처리는 서버 lock을 최소화 하기 위해 JobQueue 방식을 사용한다.
	public class JobSerializer
	{
		JobTimer _timer = new JobTimer();
		Queue<IJob> _jobQueue = new Queue<IJob>();
		object _lock = new object();
		bool _flush = false;

		public void PushAfter(int tickAfter, Action action) { PushAfter(tickAfter, new Job(action)); }
		public void PushAfter<T1>(int tickAfter, Action<T1> action, T1 t1) { PushAfter(tickAfter, new Job<T1>(action, t1)); }
		public void PushAfter<T1, T2>(int tickAfter, Action<T1, T2> action, T1 t1, T2 t2) { PushAfter(tickAfter, new Job<T1, T2>(action, t1, t2)); }
		public void PushAfter<T1, T2, T3>(int tickAfter, Action<T1, T2, T3> action, T1 t1, T2 t2, T3 t3) { PushAfter(tickAfter, new Job<T1, T2, T3>(action, t1, t2, t3)); }

		public void PushAfter(int tickAfter, IJob job)
		{
			_timer.Push(job, tickAfter);
		}

		public void Push(Action action) { Push(new Job(action)); }
		public void Push<T1>(Action<T1> action, T1 t1) { Push(new Job<T1>(action, t1)); }
		public void Push<T1, T2>(Action<T1, T2> action, T1 t1, T2 t2) { Push(new Job<T1, T2>(action, t1, t2)); }
		public void Push<T1, T2, T3>(Action<T1, T2, T3> action, T1 t1, T2 t2, T3 t3) { Push(new Job<T1, T2, T3>(action, t1, t2, t3)); }

		//핸들러를 Job으로 변환하여 _jobQueue 넣어준다.
		public void Push(IJob job)
		{
			lock (_lock)
			{
				_jobQueue.Enqueue(job);
			}
		}

		//게임룸에서 특정 시간 주기로 Tick이 발동되며 이 함수를 호출한다.
		//_jobQueue에 쌓여있는 것들을 차례로 실행한다.
		public void Flush()
		{
			_timer.Flush();

			while (true)
			{
				IJob job = Pop();
				if (job == null)
					return;

				job.Execute();
			}
		}

		IJob Pop()
		{
			lock (_lock)
			{
				if (_jobQueue.Count == 0)
				{
					_flush = false;
					return null;
				}
				return _jobQueue.Dequeue();
			}
		}
	}
}
