using System.Collections;
using System.Collections.Generic;
using UnityEngine;


namespace VSX.Utilities
{
    /// <summary>
    /// Base class for a component that saves and loads data as a string.
    /// </summary>
    public abstract class SaveLoadController : MonoBehaviour
    {
        [Tooltip("Whether to send debug information to the console during saving and loading.")]
        [SerializeField]
        protected bool debug = false;


        /// <summary>
        /// Save data in string format.
        /// </summary>
        /// <param name="saveData">The data to save.</param>
        public virtual void Save(string saveData) { }


        /// <summary>
        /// Load data into string format.
        /// </summary>
        /// <returns>The loaded data.</returns>
        public virtual string Load() { return ""; }


        /// <summary>
        /// Delete the saved data.
        /// </summary>
        public virtual void DeleteSaveData() { }
    }
}
