﻿using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Xml.Linq;

namespace SuperImageEvolver {
    public sealed class TaskState {
        public int Shapes, Vertices;
        public int ImageWidth, ImageHeight;

        public DNA BestMatch;

        public ProjectOptions ProjectOptions = new ProjectOptions();
        public int ImprovementCounter, MutationCounter;

        public Bitmap OriginalImage;
        public Bitmap WorkingImageCopy;
        public Bitmap WorkingImageCopyClone;
        public BitmapData WorkingImageData;
        public string ProjectFileName;

        public readonly List<PointF> MutationDataLog = new List<PointF>();

        public IInitializer Initializer = new SegmentedInitializer(Color.Black);
        public IMutator Mutator = new HardMutator();
        public IEvaluator Evaluator = new RGBEvaluator( false );

        public DateTime TaskStart;
        public DateTime LastImprovementTime;
        public long LastImprovementMutationCount;
        public Point ClickLocation;

        public const int FormatVersion = 1;

        public readonly object ImprovementLock = new object();

        public readonly Dictionary<MutationType, int> MutationCounts = new Dictionary<MutationType, int>();
        public readonly Dictionary<MutationType, double> MutationImprovements = new Dictionary<MutationType, double>();
        

        public void SetEvaluator( IEvaluator newEvaluator ) {
            lock( ImprovementLock ) {
                if( OriginalImage != null && BestMatch != null ) {
                    using( Bitmap testCanvas = new Bitmap( ImageWidth, ImageHeight ) ) {
                        newEvaluator.Initialize( this );
                        BestMatch.Divergence = newEvaluator.CalculateDivergence( testCanvas, BestMatch, this, 1 );
                    }
                }
                Evaluator = newEvaluator;
            }
        }


        public NBTCompound SerializeNBT() {
            NBTCompound tag = new NBTCompound( "SuperImageEvolver" );
            tag.Append( "FormatVersion", FormatVersion );
            tag.Append( "Shapes", Shapes );
            tag.Append( "Vertices", Vertices );
            tag.Append( "ImprovementCounter", ImprovementCounter );
            tag.Append( "MutationCounter", MutationCounter );
            tag.Append( "ElapsedTime", DateTime.UtcNow.Subtract( TaskStart ).Ticks );

            tag.Append( ProjectOptions.SerializeNBT() );

            tag.Append( BestMatch.SerializeNBT("BestMatch") );

            NBTag initializerTag = ModuleManager.WriteModule( "Initializer", Initializer );
            tag.Append( initializerTag );

            NBTag mutatorTag = ModuleManager.WriteModule( "Mutator", Mutator );
            tag.Append( mutatorTag );

            NBTag evaluatorTag = ModuleManager.WriteModule( "Evaluator", Evaluator );
            tag.Append( evaluatorTag );

            byte[] imageData;
            using( MemoryStream ms = new MemoryStream() ) {
                lock( OriginalImage ) {
                    OriginalImage.Save( ms, ImageFormat.Png );
                }
                ms.Flush();
                imageData = new byte[ms.Length];
                Buffer.BlockCopy( ms.GetBuffer(), 0, imageData, 0, imageData.Length );
            }

            tag.Append( "ImageData", imageData );

            List<NBTCompound> statTags = new List<NBTCompound>();
            foreach( MutationType mtype in Enum.GetValues( typeof( MutationType ) ) ) {
                NBTCompound stat = new NBTCompound( "MutationTypeStat" );
                stat.Append( "Type", mtype.ToString() );
                stat.Append( "Count", MutationCounts[mtype] );
                stat.Append( "Sum", MutationImprovements[mtype] );
                statTags.Add( stat );
            }
            var stats = new NBTList( "MutationStats", NBTType.Compound, statTags.ToArray() );
            tag.Append( stats );

            return tag;
        }

        public TaskState( NBTag tag ) {
            if( FormatVersion != tag["FormatVersion"].GetInt() ) throw new FormatException( "Incompatible format." );
            Shapes = tag["Shapes"].GetInt();
            Vertices = tag["Vertices"].GetInt();
            ImprovementCounter = tag["ImprovementCounter"].GetInt();
            MutationCounter = tag["MutationCounter"].GetInt();
            TaskStart = DateTime.UtcNow.Subtract( TimeSpan.FromTicks( tag["ElapsedTime"].GetLong() ) );

            ProjectOptions = new ProjectOptions( tag["ProjectOptions"] );

            BestMatch = new DNA( tag["BestMatch"] );

            Initializer = (IInitializer)ModuleManager.ReadModule( tag["Initializer"] );
            Mutator = (IMutator)ModuleManager.ReadModule( tag["Mutator"] );
            Evaluator = (IEvaluator)ModuleManager.ReadModule( tag["Evaluator"] );

            byte[] imageBytes = tag["ImageData"].GetBytes();
            using( MemoryStream ms = new MemoryStream( imageBytes ) ) {
                OriginalImage = new Bitmap( ms );
            }

            var statsTag = (NBTList)tag["MutationStats"];
            foreach( NBTag stat in statsTag ) {
                MutationType mutationType = (MutationType)Enum.Parse( typeof( MutationType ), stat["Type"].GetString() );
                MutationCounts[mutationType] = stat["Count"].GetInt();
                MutationImprovements[mutationType] = stat["Sum"].GetDouble();
            }
        }


        public TaskState() {
            foreach( MutationType mutype in Enum.GetValues( typeof( MutationType ) ) ) {
                MutationCounts[mutype] = 0;
                MutationImprovements[mutype] = 0;
            }
        }


        public XDocument SerializeSVG() {
            XDocument doc = new XDocument();
            XNamespace svg = "http://www.w3.org/2000/svg";
            XElement root = new XElement( svg+"svg" );
            root.Add( new XAttribute( "xmlns", svg ) );
            root.Add( new XAttribute( XNamespace.Xmlns + "xlink", "http://www.w3.org/1999/xlink" ) );
            root.Add( new XAttribute( "width", ImageWidth ) );
            root.Add( new XAttribute( "height", ImageHeight ) );
            DNA currentBestMatch = BestMatch;
            foreach( Shape shape in currentBestMatch.Shapes ) {
                root.Add( shape.SerializeSVG( svg ) );
            }
            doc.Add( root );
            
            return doc;
        }
    }
}
