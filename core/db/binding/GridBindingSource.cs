﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows.Forms;
using System.ComponentModel;
using xwcs.core.db.model;
using System.Diagnostics;
using DevExpress.XtraDataLayout;

namespace xwcs.core.db.binding
{
	using attributes;
	using DevExpress.XtraEditors;
	using DevExpress.XtraEditors.Container;
	using DevExpress.XtraEditors.Repository;
	using DevExpress.XtraGrid;
	using DevExpress.XtraGrid.Views.Base;
	using DevExpress.XtraGrid.Views.Grid;
	using evt;
	using System.Collections;
	using System.Data;
	using System.Reflection;




	public class GridBindingSource : BindingSource, IDisposable, IDataBindingSource
	{
		private static manager.ILogger _logger =  manager.SLogManager.getInstance().getClassLogger(typeof(GridBindingSource));

		private IEditorsHost _editorsHost = null;
        private IGridAdapter _target = null;
        private Type  _dataType = null;
		

		private Dictionary<string, IList<CustomAttribute>> _attributesCache = new Dictionary<string, IList<CustomAttribute>>();
		private Dictionary<string, RepositoryItem> _repositories = new Dictionary<string, RepositoryItem>();
		private bool _gridIsConnected = false;

		//For type DataTable;
		private bool _isTypeDataTable = false;

        // hold last position before position changed occurs
        private int _oldPosition = -1;


		public Dictionary<string, IList<CustomAttribute>> AttributesCache { get { return _attributesCache; } }

		//if true it will handle eventual column text override
		public bool HandleCustomColumnDisplayText { get; set; } = false;


		public GridBindingSource() : this((IEditorsHost)null) { }
		public GridBindingSource(IContainer c) : this(null, c) { }
		public GridBindingSource(object o, string s) : this(null, o, s) { }
		public GridBindingSource(IEditorsHost eh) : base() { start(eh); }
		public GridBindingSource(IEditorsHost eh, IContainer c) : base(c){ start(eh); }
		public GridBindingSource(IEditorsHost eh, object o, string s) : base(o, s){ start(eh); }

		private void start(IEditorsHost eh)
		{
			_editorsHost = eh;
			if (_editorsHost != null && _editorsHost.FormSupport != null)
			{
				_editorsHost.FormSupport.AddBindingSource(this);
			}
        }
        
        #region IDisposable Support
        protected bool disposedValue = false; // To detect redundant calls
		protected override void Dispose(bool disposing)
		{
			if (!disposedValue)
			{
				if (disposing)
				{
					//disconnect events in any case
					if (_target != null)
					{
                        _target.DataSourceChanged -= GridDataSourceChanged;
						_target.CustomRowCellEditForEditing -= CustomRowCellEditForEditingHandler;
                        _target.ShownEditor -= EditorShownHandler;
                        _target.CustomColumnDisplayText -= CustomColumnDisplayText; //try remove anyway
                        _target.ListSourceChanged -= DataController_ListSourceChanged;
                        _target.CellValueChanged -= _target_CellValueChanged;

                        //remove eventual grid cells editor repositories from grid
                        foreach (RepositoryItem ri in _repositories.Values)
						{
                            _target.RepositoryItems.Remove(ri);
						}
                        _target = null;
					}
					if (DataSource != null)
					{
						DataSource = null;
					}
					//clear cached attributes and repositories
					resetAttributes();

					ListChanged -= handleListItemChanged;
				}

				// TODO: free unmanaged resources (unmanaged objects) and override a finalizer below.
				// TODO: set large fields to null.

				disposedValue = true;
				//call inherited
				base.Dispose(disposing);
			}
		}
		#endregion


        public Type DataType
        {
            get
            {
                return _dataType;
            }
			set
			{
				_dataType = value;
			}
        }
		
		public IEditorsHost EditorsHost
		{
			get
			{
				return _editorsHost;
			}
		}

		public new object DataSource {
			get {
				return base.DataSource; 
			}

			set {
                if (value == null)
                {
                    // just empty grid rows
                    if(!ReferenceEquals(null, base.DataSource))
                    {
                        base.DataSource = null;
                    }

                    // if grid was not connected , manage remove useless columns
                    // we remove default columns, or columns present ingrid
                    if (!_gridIsConnected)
                    {
                        if (!ReferenceEquals(null, _target))
                        {
                            _target.ClearColumns();
                        }
                    }

                    return;
                }

				Type t = null;

				object tmpDs = null;

				//read annotations
				//here it depends what we have as DataSource, it can be Object, Type or IList Other we will ignore
				BindingSource bs = value as BindingSource;
				if (bs == null)
				{
					//no binding source
					tmpDs = value;
				}
				else
				{
					tmpDs = bs.DataSource;
				}

				Type tmpT = tmpDs as Type;
				if (tmpT == null)
				{
					//lets try another way, maybe IList
					if (tmpDs is IList)
					{
						//try to obtain element type
						t = (tmpDs as IList).GetType().GetGenericArguments()[0];
					}
					else if (tmpDs is IEnumerable)
					{
						//try to obtain element type
						t = (tmpDs as IEnumerable).GetType().GetGenericArguments()[0];
					}
					else if (tmpDs is DataTable)
					{
						//Do nothing because type is set by _datatype setter						
						_isTypeDataTable = true;
					}
					else if (tmpDs is IListSource)
					{
						//try to obtain element type
						t = (tmpDs as IListSource).GetType().GetGenericArguments()[0];
					}
					else
					{
						//it should be plain object and try to take type
						if ((tmpDs as object) != null)
						{
							t = tmpDs.GetType();
						}
						else
						{
							_logger.Error("Missing DataSource for grid layout");
							return; // no valid binding arrived so we skip 
						}
					}
				}
				else {
					t = tmpT;
				}

                // now when we know type we reset old handler
                if (!ReferenceEquals(null, _dataType))
                {
                    ListChanged -= handleListItemChanged;
                }

                // make generic Structure watch basing on type of DataSource element
                _oldPosition = -1;

				// _dataType musty be set before DS cause event can come in not correct order!!!!
				// we use timer on form with force message queue execution
				if (!ReferenceEquals(null, t))
				{
					_dataType = t;
				}
                ForceInitializeGrid();
				_logger.Debug("{0} [{1}]", "Before datasource set", DataType?.Name);
				base.DataSource = value;
				_logger.Debug("{0} [{1}]", "After datasource set", DataType?.Name);
				

                // now when we know type we set new handler
                if(!ReferenceEquals(null, _dataType))
                {
                    ListChanged += handleListItemChanged;
                }            

				//TODO : check type -> expception
			}
		}

        public void ForceInitializeGrid()
        {
            if (_target != null && !_target.IsReady)
            {
				_logger.Debug("{0} [{1}]", "Before _target.ForceInitialize()", DataType?.Name);
				_target.ForceInitialize(); // we need grid to initialize (it should be set in invisible component)
				_logger.Debug("{0} [{1}]", "After _target.ForceInitialize()", DataType?.Name);
			}
        }

		public void AttachToGrid(GridControl g) {

			_logger.Debug("{0} [{1}]", "AttachToGrid", DataType?.Name);
#if DEBUG_TRACE_LOG_ON
			_logger.Debug("Set-GRID : New");
#endif
			//first disconnect eventual old one
			if (_target != null)
			{
                _target.DataSourceChanged -= GridDataSourceChanged;
				_target.CustomRowCellEditForEditing -= CustomRowCellEditForEditingHandler;
                _target.ShownEditor -= EditorShownHandler;
                _target.CustomColumnDisplayText -= CustomColumnDisplayText;
                _target.ListSourceChanged -= DataController_ListSourceChanged;
                _target.CellValueChanged -= _target_CellValueChanged;
            }
            _target = new GridAdapter(g);
            _target.AutoPopulateColumns = true;
            _target.ListSourceChanged += DataController_ListSourceChanged;
            _target.DataSourceChanged += GridDataSourceChanged;
			//connect
			_target.DataSource = this;
		}

		public void AttachToTree(DevExpress.XtraTreeList.TreeList tree)
		{


#if DEBUG_TRACE_LOG_ON
			_logger.Debug("Set-GRID : New");
#endif
			//first disconnect eventual old one
			if (_target != null)
			{
				_target.DataSourceChanged -= GridDataSourceChanged;
				_target.CustomRowCellEditForEditing -= CustomRowCellEditForEditingHandler;
				_target.ShownEditor -= EditorShownHandler;
				_target.CustomColumnDisplayText -= CustomColumnDisplayText;
				_target.ListSourceChanged -= DataController_ListSourceChanged;
                _target.CellValueChanged -= _target_CellValueChanged;
            }
			_target = new TreeListAdapter(tree);
			_target.AutoPopulateColumns = true;
			_target.ListSourceChanged += DataController_ListSourceChanged;
			_target.DataSourceChanged += GridDataSourceChanged;
			//connect
			_target.DataSource = this;

		}


		private void DataController_ListSourceChanged(object sender, EventArgs e)
		{
			ConnectGrid();
		}
		private void GridDataSourceChanged(object sender, EventArgs e)
		{
			ConnectGrid();
		}

		public void addNewRecord(object rec)
		{
			AddNew();
			Current.CopyFrom(rec);
		}

        public void setCurrentRecord(object rec)
		{
			Current.CopyFrom(rec);
		}

		
		private void applyAttributes(PropertyInfo pi)
		{
			IEnumerable<CustomAttribute> attrs = ReflectionHelper.GetCustomAttributesFromPath(_dataType, pi.Name);
			IList<CustomAttribute> ac = new List<CustomAttribute>();
			foreach (CustomAttribute a in attrs)
			{
				IColumnAdapter gc = null;
				gc = _target.ColumnByFieldName(pi.Name);
				if (gc.ColumnEdit == null)
				{
					_logger.Debug("{0} [{1}]", "ColumnEdit is null", DataType?.Name);
				}

				GridColumnPopulated gcp = new GridColumnPopulated { FieldName = pi.Name, RepositoryItem = null, Column = gc };
				a.applyGridColumnPopulation(this, gcp);
				RepositoryItem ri = gcp.RepositoryItem;
				ac.Add(a as CustomAttribute);
				if (ri != null)
				{
					_target.RepositoryItems.Add(ri);
					_repositories[pi.Name] = ri;
					if (gc != null) gc.ColumnEdit = ri;
				}
			}
			if (ac.Count > 0)
				_attributesCache[pi.Name] = ac;
		}
        
		private void ConnectGrid() 
		{
			_logger.Debug("{0} [{1}]", "Enter ConnectGrid", DataType?.Name);
			if (_target != null && !_gridIsConnected && _dataType != null && _target.IsReady)
			{
				_logger.Debug("{0} [{1}]", "Working ConnectGrid", DataType?.Name);
				// check columns loaded
				if (_target.ColumnsCount() == 0)
				{
					_target.PopulateColumns();

				}

				if (!_isTypeDataTable)
				{
					PropertyInfo[] pis = _dataType.GetProperties(BindingFlags.Instance | BindingFlags.Public);
					foreach (PropertyInfo pi in pis)
					{
						applyAttributes(pi);
					}
				}
				else 
				{
					DataTable dt = base.DataSource as DataTable;
					if (dt != null)
					{
						PropertyInfo[] pis = _dataType.GetProperties(BindingFlags.Instance | BindingFlags.Public);
						foreach (PropertyInfo pi in pis)
						{
							if (dt.Columns[pi.Name]	!= null)
							{
								applyAttributes(pi);
							}
						}
					}
				}
				//attach CustomRowCellEditForEditing event too
				_target.CustomRowCellEditForEditing += CustomRowCellEditForEditingHandler;
                _target.ShownEditor += EditorShownHandler;
				

				_target.CellValueChanged += _target_CellValueChanged;
				

				if (HandleCustomColumnDisplayText)
                    _target.CustomColumnDisplayText += CustomColumnDisplayText;
				
				_gridIsConnected = true;
			}	
		}

		private void _target_CellValueChanged(object sender, CellValueChangedEventArgs e)
		{
			_target.PostChanges();
		}

		protected virtual void GetFieldDisplayText(object sender, CustomColumnDisplayTextEventArgs e) {
			//just to be overloaded if necessary	
		}
        

		private void CustomColumnDisplayText(object sender, CustomColumnDisplayTextEventArgs e){

            if (_attributesCache.ContainsKey((e.GridLike ? e.Column.FieldName : e.TreeColumn.FieldName)))
            {
                foreach (CustomAttribute a in _attributesCache[(e.GridLike ? e.Column.FieldName : e.TreeColumn.FieldName)])
                {
                    a.applyGetFieldDisplayText(this, e);
                }
            }

            // eventual override
            GetFieldDisplayText(sender, e);
        }

		private void EditorShownHandler(object sender, EventArgs e) 
		{
			ColumnView view;
			DevExpress.XtraTreeList.TreeList treeList;

			if ((view = sender as ColumnView) != null)
			{
				Control editor = view.ActiveEditor;
				RepositoryItem be = ((BaseEdit)editor).Properties;
				ViewEditorShownEventArgs vsea = new ViewEditorShownEventArgs
				{
					Control = editor,
					View = view,
					FieldName = view.FocusedColumn.FieldName,
					RepositoryItem = be
				};

				if (_attributesCache.ContainsKey(vsea.FieldName))
				{
					foreach (CustomAttribute a in _attributesCache[vsea.FieldName])
					{
						a.applyCustomEditShown(this, vsea);
					}
				}			
			}
			else	
			if ((treeList = sender as DevExpress.XtraTreeList.TreeList) != null)
			{
				Control editor = treeList.ActiveEditor;
				RepositoryItem be = ((BaseEdit)editor).Properties;
				ViewEditorShownEventArgs vsea = new ViewEditorShownEventArgs
				{
					Control = editor,
					TreeList = treeList,
					FieldName = treeList.FocusedColumn.FieldName,
					RepositoryItem = be
				};

				if (_attributesCache.ContainsKey(vsea.FieldName))
				{
					foreach (CustomAttribute a in _attributesCache[vsea.FieldName])
					{
						a.applyCustomEditShown(this, vsea);
					}
				}
			}
		}

		private void CustomRowCellEditForEditingHandler(object sender, CustomRowCellEditEventArgs e) 
		{
			if (_repositories.ContainsKey((e.GridLike?e.Column.FieldName:e.TreeColumn.FieldName)))
			{
				e.RepositoryItem = _repositories[(e.GridLike ? e.Column.FieldName : e.TreeColumn.FieldName)];
			}
			if (_attributesCache.ContainsKey((e.GridLike ? e.Column.FieldName : e.TreeColumn.FieldName)))
			{
				foreach (CustomAttribute a in _attributesCache[(e.GridLike ? e.Column.FieldName : e.TreeColumn.FieldName)])
				{
					a.applyCustomRowCellEdit(this, e);
				}
			}
		}

		//if there is change or we dispose we need reset attributes
		private void resetAttributes()
		{
			_attributesCache.Values.ToList().ForEach(e => { e.ToList().ForEach(a => a.unbind(this)); e.Clear(); });
			_attributesCache.Clear();
			//clear repositories cache	
			_repositories.Values.ToList().ForEach(r => r.Dispose());				
			_repositories.Clear();
		}


		protected virtual void resetSlavesOfModifiedProperty(ResetSlavesAttribute att) {
			//should be overridden

		}

		private void handleListItemChanged(object sender, ListChangedEventArgs args)
		{
			if(args.ListChangedType == ListChangedType.ItemChanged)
			{
				if(args.PropertyDescriptor != null) {
					IEnumerable<CustomAttribute> attrs = ReflectionHelper.GetCustomAttributesFromPath(_dataType, args.PropertyDescriptor.Name);
					var ra = attrs.OfType<ResetSlavesAttribute>();
					if(ra.Count() == 1) {
						//we need reset slaves chain
						resetSlavesOfModifiedProperty(ra.First());
					}
					
#if DEBUG_TRACE_LOG_ON
					_logger.Debug("Item changed! " + args.PropertyDescriptor.Name);
#endif
				}
			}
		}

        public Control GetControlByModelProperty(string ModelropertyName)
        {
            return null;
        }

        public void SuspendLayout()
        {
            return;
        }

        public void ResumeLayout()
        {
            return;
        }


        protected override void OnPositionChanged(EventArgs e)
        {
            if (_oldPosition == Position) return; // skip call on the same line
            base.OnPositionChanged(e);
            //store current in old
            _oldPosition = Position; // this means first one will not exist!!!
        }

        public bool ChangeLayout(string LayoutSuffix)
        {
            throw new NotImplementedException();
        }

        public int LastPosition
        {
            get
            {
                return _oldPosition;
            }
        }

        
    }
}
