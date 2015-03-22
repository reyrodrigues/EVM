﻿'use strict';

angular.module('app')
  .factory('listController', ['$state', 'backendService', 'toaster', 'gettext', 'breeze', 'dialogs','$localStorage',
      function ($state, backendService, toaster, gettext, breeze, dialogs, $localStorage) {
          var listController = function ($scope, settings) {
              var storageSetting = $state.current.name + 'GridSettings';

              if (!settings.columnDefs) {
                  settings.columnDefs = [
                      {
                          field: gettext("Name"),
                          cellTemplate: '<div class="ngCellText" ng-class="col.colIndex()"><span ng-cell-text><a href ui-sref="' +
                          settings.editState + '({ id: row.getProperty(\'Id\') })">{{COL_FIELD}}</a></span></div>'
                      }
                  ];
              }

              $scope.loadGridData = function () {
                  setTimeout(function () {
                      var data;

                      var fields = [];
                      for (var i = 0; i < $scope.gridOptions.sortInfo.fields.length; i++) {
                          var ordering = $scope.gridOptions.sortInfo.fields[i] + ($scope.gridOptions.sortInfo.directions[i] == "desc" ? " desc" : "");

                          fields.push(ordering);
                      }

                      var order = fields.join(',');

                      var entityQuery = new breeze.EntityQuery(settings.collectionType);

                      if (settings.expand) {
                          entityQuery = entityQuery.expand(settings.expand);
                      }

                      if (order) {
                          entityQuery = entityQuery
                              .orderBy(order);
                      }

                      entityQuery = entityQuery
                          .skip($localStorage[storageSetting].pageSize * ($localStorage[storageSetting].currentPage - 1))
                          .take($localStorage[storageSetting].pageSize)
                          .inlineCount(true)
                          .using(backendService);

                      if ($scope.filter) {
                          entityQuery = entityQuery.where($scope.filter);
                      }

                      entityQuery
                          .execute()
                          .then(function (res) {
                              $scope.totalServerItems = res.inlineCount;
                              $scope.list = res.results.map(function (r) {
                                  return r;
                              });

                              if (!$scope.$$phase) {
                                  $scope.$apply();
                              }
                          })
                      .catch(function () { console.log(arguments); });
                  }, 100);
              };

              var watchFunction = function () {
                  $localStorage[storageSetting].pageSize = parseInt($scope.pagingOptions.pageSize);
                  $localStorage[storageSetting].currentPage = parseInt($scope.pagingOptions.currentPage);
                  $localStorage[storageSetting].sortInfo = $scope.gridOptions.sortInfo;
                  $scope.loadGridData();
              };

              if (!angular.isDefined($localStorage[storageSetting])) {
                  $localStorage[storageSetting] = {
                      pageSize: 250,
                      currentPage: 1,
                      sortInfo: {
                          fields: ['Id'],
                          directions: ['asc']
                      }
                  };
              }

              $scope.totalServerItems = 0;
              $scope.pagingOptions = {
                  pageSizes: [250, 500, 1000],
                  pageSize: $localStorage[storageSetting].pageSize,
                  currentPage: $localStorage[storageSetting].currentPage
              };

              $scope.gridOptions = {
                  data: 'list',
                  enablePaging: true,
                  showFooter: true,
                  rowHeight: 36,
                  headerRowHeight: 36,
                  totalServerItems: 'totalServerItems',
                  sortInfo: $localStorage[storageSetting].sortInfo,
                  pagingOptions: $scope.pagingOptions,
                  enableRowSelection: false,
                  useExternalSorting: true,
                  columnDefs: settings.columnDefs
              };

              $scope.$watch('pagingOptions', watchFunction, true);
              $scope.$watch('gridOptions.sortInfo', watchFunction, true);

          };

          return listController;
  }]);

angular.module('app')
  .factory('editController', ['$state', 'backendService', 'toaster', 'gettext', 'breeze', 'dialogs', '$q',
      function ($state, backendService, toaster, gettext, breeze, dialogs, $q) {
          var editController = function ($scope, settings) {
              $scope.isEditing = false;


              $scope.save = function (andContinue) {
                  $scope.isEditing = false;

                  backendService.saveChanges([$scope.entity]).then(function (ne) {
                      toaster.pop('success', gettext('Success'), gettext('Record successfully saved.'));

                      if (!andContinue)
                          $state.go(settings.listState);
                  }).catch(function (error) {
                      console.log(arguments);

                      toaster.pop('error', gettext('Error'), error);
                  });
              };

              $scope.delete = function () {
                  var dlg = dialogs.confirm(gettext("Confirm"), gettext("Are you sure you would like to delete this record? This operation cannot be reversed."));
                  dlg.result.then(function () {
                      $scope.entity.entityAspect.setDeleted();
                      $scope.isEditing = false;

                      backendService.saveChanges([$scope.entity]).then(function () {
                          toaster.pop('success', gettext('Success'), gettext('Record successfully deleted.'));
                          $state.go(settings.listState);
                      }).catch(function (error) {
                          console.log(arguments);

                          toaster.pop('error', gettext('Error'), error);
                      });
                  });
              };

              $scope.startEditing = function () {
                  $scope.isEditing = true;
              };
              $scope.endEditing = function () {
                  $scope.isEditing = false;
              };

              $scope.loadData = function () {
                  var defer = $q.defer();
                  var query = new breeze.EntityQuery(settings.collectionType)
                      .using(backendService);

                  if (settings.expand) {
                      query = query.expand(settings.expand);
                  }

                  query.where("Id", "==", $state.params.id)
                      .take(1)
                      .execute()
                      .then(function (res) {
                          if (res.results) {
                              var entity = res.results.pop();

                              $scope.entity = entity;
                              defer.resolve(entity);
                          }
                      }).catch(function () {
                          console.log(arguments);
                      });

                  return defer.promise;
              };
          };

          return editController;
      }]);

angular.module('app')
  .factory('createController', ['$state', 'backendService', 'toaster', 'gettext',
      function ($state, backendService, toaster, gettext) {
          var createController = function ($scope, settings) {
              $scope.isEditing = true;
              $scope.isNew = true;
              $scope.entity = backendService.createEntity(settings.entityType, settings.defaults);

              $scope.save = function () {
                  backendService.saveChanges([$scope.entity]).then(function (ne) {
                      toaster.pop('success', gettext('Success'), gettext('Record successfully saved.'));
                      $state.go(settings.editState, { id: ne.entities[0].Id });
                  }).catch(function (error) {
                      toaster.pop('error', gettext('Error'), error);
                  });
              };
          };

          return createController;
      }]
  );

angular.module('app')
  .factory('subGrid', ['$state', 'backendService', 'toaster', 'gettext',
      function ($state, backendService, toaster, gettext) {
          var subGrid = function ($scope, settings) {
              var storageSetting = $state.current.name + settings.collectionType + 'GridSettings';
              var columnDefs = settings.columnDefs;

              if (!columnDefs) {
                  columnDefs = [{ field: "Name", displayName: "Name" }];
              }

              $scope[settings.collectionType] = [];
              $scope[settings.collectionType + 'PagingOptions'] = {
                  pageSizes: [10, 20, 1000],
                  pageSize: 10,
                  currentPage: 1
              };
              $scope['gridOptions' + settings.collectionType] = {
                  data: settings.collectionType,
                  enablePaging: true,
                  showFooter: true,
                  rowHeight: 36,
                  headerRowHeight: 36,
                  totalServerItems: settings.collectionType + '_Count',
                  sortInfo: {
                      fields: ['id'],
                      directions: ['asc']
                  },
                  pagingOptions: $scope[settings.collectionType + 'PagingOptions'],
                  enableRowSelection: false,
                  useExternalSorting: true,
                  columnDefs: columnDefs
              };


              $scope[settings.collectionType + 'LoadGrid'] = function (pageSize, page) {
                  if (!$scope.entity || !$scope.entity.Id) {
                      return;
                  }

                  setTimeout(function () {
                      var fields = [];
                      var gridOptions = $scope['gridOptions' + settings.collectionType];
                      var pagingOptions = $scope[settings.collectionType + 'PagingOptions'];

                      for (var i = 0; i < gridOptions.sortInfo.fields.length; i++) {
                          var ordering = gridOptions.sortInfo.fields[i] + (gridOptions.sortInfo.directions[i] == "desc" ? " desc" : "");

                          fields.push(ordering);
                      }

                      var order = fields.join(',');

                      var entityQuery = new breeze.EntityQuery(settings.collectionType);
                      if (settings.expand) entityQuery = entityQuery.expand(settings.expand);
                      if (order) entityQuery = entityQuery.orderBy(order);


                      entityQuery = entityQuery
                          .skip(pagingOptions.pageSize * (pagingOptions.currentPage - 1))
                          .take(pagingOptions.pageSize)
                          .inlineCount(true)
                          .using(backendService);

                      var keyFilter = {};
                      keyFilter[settings.key] = { '==': $scope.entity.Id };

                      entityQuery = entityQuery.where(keyFilter);
                      entityQuery.execute()
                          .then(function (res) {
                              $scope[settings.collectionType + '_Count'] = res.inlineCount;
                              $scope[settings.collectionType] = res.results;
                          });
                  }, 100);
              };

              var watchFunction = function () {
                  $scope[settings.collectionType + 'LoadGrid']();
              };

              $scope.$watch(settings.collectionType + 'PagingOptions', watchFunction, true);
              $scope.$watch('gridOptions' + settings.collectionType + '.sortInfo', watchFunction, true);
          };

          return subGrid;
      }]
  );

