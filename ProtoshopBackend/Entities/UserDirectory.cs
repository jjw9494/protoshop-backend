using MongoDB.Bson.Serialization.Attributes;
using MongoDB.Bson;

namespace fileshare.Entities
{
    public class TopLevelUserObject
    {
        [BsonId]
        [BsonRepresentation(BsonType.ObjectId)]
        public ObjectId? Id { get; set; }

        public string? UserId { get; set; }
        public string? Username { get; set; }
        public List<UserDirectory>? Directory { get; set; }
    }

    public class UserDirectory
    {
        public string? ObjId { get; set; }

        public string? Name { get; set; }
        public bool IsFolder { get; set; }

        public List<UserDirectory>? ObjChildren { get; set; } 
}
}
