﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace g3
{


    /// <summary>
    /// Utility functions for DGraph2 data structure
    /// </summary>
    public static class DGraph2Util
    {


        public struct Curves
        {
            public List<Polygon2d> Loops;
            public List<PolyLine2d> Paths;
        }


        /// <summary>
        /// Decompose graph into simple polylines and polygons. 
        /// </summary>
        public static Curves ExtractCurves(DGraph2 graph)
        {
            Curves c = new Curves();
            c.Loops = new List<Polygon2d>();
            c.Paths = new List<PolyLine2d>();

            HashSet<int> used = new HashSet<int>();

            // find boundary and junction vertices
            HashSet<int> boundaries = new HashSet<int>();
            HashSet<int> junctions = new HashSet<int>();
            foreach ( int vid in graph.VertexIndices() ) {
                if (graph.IsBoundaryVertex(vid))
                    boundaries.Add(vid);
                if (graph.IsJunctionVertex(vid))
                    junctions.Add(vid);
            }

            // walk paths from boundary vertices
            foreach (int start_vid in boundaries) {
                int vid = start_vid;
                int eid = graph.GetVtxEdges(vid)[0];
                if (used.Contains(eid))
                    continue;

                PolyLine2d path = new PolyLine2d();
                path.AppendVertex(graph.GetVertex(vid));
                while ( true ) {
                    used.Add(eid);
                    Index2i next = NextEdgeAndVtx(eid, vid, graph);
                    eid = next.a;
                    vid = next.b;
                    path.AppendVertex(graph.GetVertex(vid));
                    if (boundaries.Contains(vid) || junctions.Contains(vid))
                        break;  // done!
                }
                c.Paths.Add(path);
            }

            // ok we should be done w/ boundary verts now...
            boundaries.Clear();


            foreach ( int start_vid in junctions ) {
                foreach (int outgoing_eid in graph.VtxEdgesItr(start_vid) ) {
                    if (used.Contains(outgoing_eid))
                        continue;
                    int vid = start_vid;
                    int eid = outgoing_eid;

                    PolyLine2d path = new PolyLine2d();
                    path.AppendVertex(graph.GetVertex(vid));
                    while (true) {
                        used.Add(eid);
                        Index2i next = NextEdgeAndVtx(eid, vid, graph);
                        eid = next.a;
                        vid = next.b;
                        path.AppendVertex(graph.GetVertex(vid));
                        if (eid == int.MaxValue || junctions.Contains(vid))
                            break;  // done!
                    }
                    c.Paths.Add(path);
                }

            }


            // all that should be left are continuous loops...
            foreach ( int start_eid in graph.EdgeIndices() ) {
                if (used.Contains(start_eid))
                    continue;

                int eid = start_eid;
                Index2i ev = graph.GetEdgeV(eid);
                int vid = ev.a;

                Polygon2d poly = new Polygon2d();
                poly.AppendVertex(graph.GetVertex(vid));
                while (true) {
                    used.Add(eid);
                    Index2i next = NextEdgeAndVtx(eid, vid, graph);
                    eid = next.a;
                    vid = next.b;
                    poly.AppendVertex(graph.GetVertex(vid));
                    if (eid == int.MaxValue || junctions.Contains(vid))
                        throw new Exception("how did this happen??");
                    if (used.Contains(eid))
                        break;
                }
                poly.RemoveVertex(poly.VertexCount - 1);
                c.Loops.Add(poly);
            }


            return c;
        }





        /// <summary>
        /// Find and remove any junction (ie valence>2) vertices of the graph.
        /// At a junction, the pair of best-aligned (ie straightest) edges are left 
        /// connected, and all the other edges are disconnected
        /// 
        /// [TODO] currently there is no DGraph2.SetEdge(), so the 'other' edges
        /// are deleted and new edges inserted. Hence, edge IDs are not preserved.
        /// </summary>
        public static int DisconnectJunctions(DGraph2 graph)
        {
            List<int> junctions = new List<int>();

            // find all junctions
            foreach (int vid in graph.VertexIndices()) {
                if (graph.IsJunctionVertex(vid))
                    junctions.Add(vid);
            }


            foreach (int vid in junctions) {
                Vector2d v = graph.GetVertex(vid);
                int[] nbr_verts = graph.VtxVerticesItr(vid).ToArray();

                // find best-aligned pair of edges connected to vid
                Index2i best_aligned = Index2i.Max; double max_angle = 0;
                for (int i = 0; i < nbr_verts.Length; ++i) {
                    for (int j = i + 1; j < nbr_verts.Length; ++j) {
                        double angle = Vector2d.AngleD(
                            (graph.GetVertex(nbr_verts[i]) - v).Normalized,
                            (graph.GetVertex(nbr_verts[j]) - v).Normalized);
                        angle = Math.Abs(angle);
                        if (angle > max_angle) {
                            max_angle = angle;
                            best_aligned = new Index2i(nbr_verts[i], nbr_verts[j]);
                        }
                    }
                }

                // for nbr verts that are not part of the best_aligned edges,
                // we remove those edges and add a new one connected to a new vertex
                for (int k = 0; k < nbr_verts.Length; ++k) {
                    if (nbr_verts[k] == best_aligned.a || nbr_verts[k] == best_aligned.b)
                        continue;
                    int eid = graph.FindEdge(vid, nbr_verts[k]);
                    graph.RemoveEdge(eid, true);
                    if (graph.IsVertex(nbr_verts[k])) {
                        Vector2d newpos = Vector2d.Lerp(graph.GetVertex(nbr_verts[k]), v, 0.99);
                        int newv = graph.AppendVertex(newpos);
                        graph.AppendEdge(nbr_verts[k], newv);
                    }
                }
            }

            return junctions.Count;
        }






        /// <summary>
        /// If vid has two or more neighbours, returns uniform laplacian, otherwise returns vid position
        /// </summary>
        public static Vector2d VertexLaplacian(DGraph2 graph, int vid, out bool isValid)
        {
            Vector2d v = graph.GetVertex(vid);
            Vector2d centroid = Vector2d.Zero;
            int n = 0;
            foreach (int vnbr in graph.VtxVerticesItr(vid)) {
                centroid += graph.GetVertex(vnbr);
                n++;
            }
            if (n == 2) {
                centroid /= n;
                isValid = true;
                return centroid-v;
            }
            isValid = false;
            return v;
        }




        /// <summary>
        /// If we are at edge eid, which as one vertex prev_vid, find 'other' vertex, and other edge connected to that vertex,
        /// and return pair [next_edge, shared_vtx]
        /// Returns [int.MaxValue, shared_vtx] if shared_vtx is not valence=2   (ie stops at boundaries and complex junctions)
        /// </summary>
        public static Index2i NextEdgeAndVtx(int eid, int prev_vid, DGraph2 graph)
        {
            Index2i ev = graph.GetEdgeV(eid);
            if (ev.a == DGraph2.InvalidID)
                return Index2i.Max;
            int next_vid = (ev.a == prev_vid) ? ev.b : ev.a;

            if (graph.GetVtxEdgeCount(next_vid) != 2)
                return new Index2i(int.MaxValue, next_vid);

            foreach (int next_eid in graph.VtxEdgesItr(next_vid)) {
                if (next_eid != eid)
                    return new Index2i(next_eid, next_vid);
            }
            return Index2i.Max;
        }


    }
}
