// $Id$

namespace Ankh
{
    public interface ISolutionExplorer : ISelectionContainer
    {
        /// <summary>
        /// Refreshes all subnodes of a specific project.
        /// </summary>
        /// <param name="project"></param>
        void Refresh( Project project );

        /// <summary>
        /// Updates the status of the given item.
        /// </summary>
        /// <param name="item"></param>
        void UpdateStatus( ProjectItem item );

        /// <summary>	 	
        /// Visits all the selected nodes.	 	
        /// </summary>	 	
        /// <param name="visitor"></param>	 	
        void VisitSelectedNodes( INodeVisitor visitor );

        /// <summary>
        /// Retrieves the resources associated with a project item.
        /// </summary>
        /// <param name="item"></param>
        /// <param name="recursive"></param>
        /// <returns></returns>
        IList GetItemResources( ProjectItem item, bool recursive );

        /// <summary>	 	
        /// Visits all the selected nodes.	 	
        /// </summary>	 	
        /// <param name="visitor"></param>	 	
        public void VisitSelectedNodes( INodeVisitor visitor );

        /// <summary>
        /// Returns the selected ProjectItem
        /// </summary>
        /// <returns></returns>
        ProjectItem GetSelectedProjectItem();
    }
}
