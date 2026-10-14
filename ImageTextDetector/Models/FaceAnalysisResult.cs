using System.Collections.Generic;

namespace ImageTextDetector.Models
{
    public class FaceAnalysisResult
    {
        public bool HasFaces { get; set; }
        public bool HasTargetFace { get; set; }
        public bool HasPerson { get; set; }
        public int FacesDetected { get; set; }
        public List<FaceMatch> Matches { get; set; }

        public FaceAnalysisResult()
        {
            HasFaces = false;
            HasTargetFace = false;
            HasPerson = false;
            FacesDetected = 0;
            Matches = new List<FaceMatch>();
        }

        public FaceAnalysisResult(bool hasFaces, bool hasTargetFace, bool hasPerson, int facesDetected, List<FaceMatch> matches)
        {
            HasFaces = hasFaces;
            HasTargetFace = hasTargetFace;
            HasPerson = hasPerson;
            FacesDetected = facesDetected;
            Matches = matches;
        }
    }
}
