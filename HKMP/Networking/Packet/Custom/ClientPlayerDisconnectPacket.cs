namespace HKMP.Networking.Packet.Custom {
    public class ClientPlayerDisconnectPacket : Packet, IPacket {

        public ushort Id { get; set; }
        public string Username { get; set; }

        public ClientPlayerDisconnectPacket() {
        }

        public ClientPlayerDisconnectPacket(Packet packet) : base(packet) {
        }

        public Packet CreatePacket() {
            Reset();
            
            Write(PacketId.PlayerDisconnect);

            Write(Id);
            Write(Username);

            WriteLength();

            return this;
        }

        public void ReadPacket() {
            Id = ReadUShort();
            Username = ReadString();
        }
    }
}