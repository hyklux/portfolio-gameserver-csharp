using System;
using System.Collections.Generic;
using System.Text;

namespace Server.Game
{
	public class RoomManager
	{
		public static RoomManager Instance { get; } = new RoomManager();

		object _lock = new object();
		Dictionary<int, GameRoom> _rooms = new Dictionary<int, GameRoom>();
		int _roomId = 1;

		//게임룸 추가
		public GameRoom Add(int mapId)
		{
			GameRoom gameRoom = new GameRoom();
			gameRoom.Push(gameRoom.Init, mapId);

			lock (_lock)
			{
				gameRoom.RoomId = _roomId;
				_rooms.Add(_roomId, gameRoom);
				_roomId++;
			}

			return gameRoom;
		}

		//게임룸 삭제
		public bool Remove(int roomId)
		{
			lock (_lock)
			{
				return _rooms.Remove(roomId);
			}
		}

		public GameRoom Find(int roomId)
		{
			lock (_lock)
			{
				GameRoom room = null;
				if (_rooms.TryGetValue(roomId, out room))
					return room;

				return null;
			}
		}
	}
}
