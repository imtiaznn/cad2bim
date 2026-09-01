namespace Cad2Bim {
    /// <summary>
    /// Narrows the search for a wall's other face.
    /// <para>
    /// Two segments can only be a wall if they are near-parallel and separated by no more than the
    /// maximum wall thickness. Both facts bucket cleanly, so instead of comparing every segment
    /// with every other one, segments are filed by heading and then by how far their midpoint sits
    /// off the origin along that heading's normal. A query only visits the neighbouring buckets.
    /// </para>
    /// <para>
    /// The index is a filter, never a decision: it returns a superset of the admissible partners
    /// and the caller still applies the real tests. Anything it excluded could not have passed
    /// them.
    /// </para>
    /// </summary>
    internal sealed class SegmentIndex {
        // Comfortably wider than the 2 degree parallelism tolerance, so a true pair is at worst one
        // bucket away.
        private const double HeadingBucketDegrees = 5.0;
        private const int HeadingBuckets = (int)(180 / HeadingBucketDegrees);

        // Near-parallel segments are not exactly parallel, so a long pair's midpoint offsets can
        // drift somewhat beyond their perpendicular distance. Two buckets of slack absorbs that.
        private const int OffsetBucketReach = 2;

        private readonly IReadOnlyList<Segment> _segments;
        private readonly double _offsetBucketSize;
        private readonly Dictionary<(int Heading, int Offset), List<int>> _buckets = new();

        public SegmentIndex(IReadOnlyList<Segment> segments, double maxThickness) {
            _segments = segments;
            _offsetBucketSize = maxThickness > 0 ? maxThickness : 1.0;

            for (int i = 0; i < segments.Count; i++) {
                int heading = HeadingBucket(segments[i]);
                var key = (heading, OffsetBucket(segments[i], heading));

                if (!_buckets.TryGetValue(key, out List<int>? bucket)) {
                    _buckets[key] = bucket = new List<int>();
                }

                bucket.Add(i);
            }
        }

        /// <summary>
        /// Indices that could pair with <paramref name="index"/>, ascending — the caller's
        /// tie-break depends on scan order, so the original's is preserved.
        /// </summary>
        public IEnumerable<int> CandidatesFor(int index) {
            Segment segment = _segments[index];
            int heading = HeadingBucket(segment);
            List<int> found = new();

            for (int dh = -1; dh <= 1; dh++) {
                // Headings wrap at 180: a line at 179 degrees and one at 1 degree are all but
                // parallel, so the first and last buckets are neighbours.
                int headingBucket = ((heading + dh) % HeadingBuckets + HeadingBuckets) % HeadingBuckets;

                // Offsets are measured against each bucket's own nominal heading, so the query's
                // offset has to be recomputed per bucket rather than carried across.
                int offset = OffsetBucket(segment, headingBucket);

                for (int deltaOffset = -OffsetBucketReach; deltaOffset <= OffsetBucketReach; deltaOffset++) {
                    if (_buckets.TryGetValue((headingBucket, offset + deltaOffset), out List<int>? bucket)) {
                        found.AddRange(bucket);
                    }
                }
            }

            found.Sort();
            return found;
        }

        private static int HeadingBucket(Segment segment) {
            int bucket = (int)Math.Floor(segment.HeadingDegrees / HeadingBucketDegrees);
            return Math.Clamp(bucket, 0, HeadingBuckets - 1);
        }

        private int OffsetBucket(Segment segment, int headingBucket) {
            double radians = (headingBucket + 0.5) * HeadingBucketDegrees * (Math.PI / 180.0);
            Point mid = segment.Mid;
            double offset = (-Math.Sin(radians) * mid.x) + (Math.Cos(radians) * mid.y);
            return (int)Math.Floor(offset / _offsetBucketSize);
        }
    }
}
