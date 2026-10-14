using System.Windows;

namespace ImageTextDetector.Models
{
    public class FaceMatch
    {
        public int Index { get; set; }
        public bool IsMatch { get; set; }
        public double Distance { get; set; }
        public double Confidence { get; set; }
        public double Tolerance { get; set; }
        public Rect BoundingBox { get; set; }

        public FaceMatch(int index, bool isMatch, double distance, double tolerance, Rect boundingBox, double confidence = 0)
        {
            Index = index;
            IsMatch = isMatch;
            Distance = distance;
            Tolerance = tolerance;
            BoundingBox = boundingBox;
            Confidence = confidence;
        }
    }
}
