using System.Windows;

namespace ImageTextDetector.Models
{
    public class TextRegion
    {
        public string Text { get; set; }
        public Rect BoundingBox { get; set; }
        public double Confidence { get; set; }

        public TextRegion(string text, Rect boundingBox, double confidence)
        {
            Text = text;
            BoundingBox = boundingBox;
            Confidence = confidence;
        }
    }
}
