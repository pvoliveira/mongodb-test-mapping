namespace mongotestmapping
{
    using System;
    using System.Collections.Generic;
    using System.Linq;
    using System.Reflection;
    using Mongo2Go;
    using MongoDB.Driver;
    using MongoDB.Bson;
    using MongoDB.Bson.Serialization;
    using MongoDB.Bson.Serialization.Attributes;
    using MongoDB.Bson.Serialization.Conventions;

    class Program
    {
        static void Main(string[] args)
        {
            var runner = MongoDbRunner.Start();
            AppDomain.CurrentDomain.UnhandledException += (o, s) => 
            {
                runner.Dispose();
            };

            AppDomain.CurrentDomain.ProcessExit += (o, s) => 
            {
                runner.Dispose();
            };

            var conventionPack = new ConventionPack
            {
                new MapReadOnlyPropertiesConvention()
            };

            ConventionRegistry.Register("Conventions", conventionPack, _ => true);

            BsonClassMap.RegisterClassMap<PhoneNumber>(m =>
            {
                m.AutoMap();
                m.MapField("value")
                    .SetElementName("value");
            });

            BsonClassMap.RegisterClassMap<Contacts>(m =>
            {
                m.AutoMap();
                m.MapField("phones")
                    .SetElementName("phones");
            });

            BsonClassMap.RegisterClassMap<Person>(m =>
            {
                m.AutoMap();
                m.MapField(p => p.Name)
                    .SetElementName("name");
                m.MapField(p => p.Contacts)
                    .SetElementName("contacts");
            });

            var client = new MongoClient(runner.ConnectionString);
            var database = client.GetDatabase(nameof(Person));
            var collection = database.GetCollection<Person>("test");

            var phone = new PhoneNumber("123-123");
            var contacts = new Contacts(new List<PhoneNumber> {  });
            var person = new Person("Paul", contacts);

            collection.InsertOne(person);

            var personDB = collection.FindSync(p => p.Id == person.Id).FirstOrDefault();

            Console.WriteLine($"{personDB?.Name}\n{string.Join(", ", personDB?.Contacts?.Phones)}");

            var filter = Builders<Person>.Filter.Eq(p => p.Id, person.Id)
                & Builders<Person>.Filter.ElemMatch(p => p.Contacts.Phones,
                    Builders<PhoneNumber>.Filter.Eq(pn => pn.Id, phone.Id));

            var setter = Builders<Person>.Update.Set(p => p.Contacts.Phones[-1].Value, "111-222");

            var newPersonDB = collection.FindOneAndUpdate(
                filter, setter);

            database.DropCollection("test");
        }
    }

    public class PhoneNumber : Entity
    {
        private string value; 
        public string Value => value;

        private PhoneNumber() : base() {}

        public PhoneNumber(string number) : base()
        {
            value = number;
        }

        public override string ToString() => value;
    }

    public class Contacts : Entity
    {
        private IList<PhoneNumber> phones;

        public IReadOnlyList<PhoneNumber> Phones { get { return phones.ToArray(); } }

        public Contacts() : base()
        {
            phones = new List<PhoneNumber>();
        }

        public Contacts(List<PhoneNumber> phones) : base()
        {
            this.phones = phones;
        }

        public void AddPhone(PhoneNumber phone)
        {
            this.phones.Add(phone);
        }
    }

    public class Person : Entity
    {
        public string Name { get; private set; }
        public Contacts Contacts { get; private set; }

        public Person(string name, Contacts contacts) : base()
        {
            Name = name;
            Contacts = contacts;
        }
    }

    public class Entity
    {
        [BsonId]
        public ObjectId Id { get; set; }

        public Entity()
        {
            Id = ObjectId.GenerateNewId();
        }
    }

    /// <summary>
    /// A convention to ensure that read-only properties are automatically mapped (and therefore serialised).
    /// </summary>
    public class MapReadOnlyPropertiesConvention : ConventionBase, IClassMapConvention
    {
        private readonly BindingFlags _bindingFlags;

        public MapReadOnlyPropertiesConvention() : this(BindingFlags.Instance | BindingFlags.Public) {}

        public MapReadOnlyPropertiesConvention(BindingFlags bindingFlags)
        {
            _bindingFlags = bindingFlags | BindingFlags.DeclaredOnly;
        }

        public void Apply(BsonClassMap classMap)
        {
            var readOnlyProperties = classMap
                .ClassType
                .GetTypeInfo()
                .GetProperties(_bindingFlags)
                .Where(p => IsReadOnlyProperty(classMap, p))
                .ToList();

            foreach (var property in readOnlyProperties)
            {
                classMap.MapMember(property);
            }
        }

        private static bool IsReadOnlyProperty(BsonClassMap classMap, PropertyInfo propertyInfo)
        {
            if (!propertyInfo.CanRead) return false;
            if (propertyInfo.CanWrite) return false; // already handled by default convention
            if (propertyInfo.GetIndexParameters().Length != 0) return false; // skip indexers

            var getMethodInfo = propertyInfo.GetMethod;

            // skip overridden properties (they are already included by the base class)
            if (getMethodInfo.IsVirtual && getMethodInfo.GetBaseDefinition().DeclaringType != classMap.ClassType) return false;

            return true;
        }
    }
}
