﻿'use strict';
angular.module('app')
    .directive('toggleEditing', ['$q', function ($q) {
    return {
        restrict: 'A',
        scope: {
            isEditing: "="
        },
        replace: true,
        link: function (scope, element, attrs) {
            var toggleEditing = function () {
                var isEditing = scope.isEditing || false;

                $('input, select, textarea', element).prop('readonly', !isEditing);

                $('.isChosen').trigger('chosen:updated');
            };

            toggleEditing();

            scope.$watch('isEditing', function () {
                toggleEditing();
            });
        }
    }
    }]);